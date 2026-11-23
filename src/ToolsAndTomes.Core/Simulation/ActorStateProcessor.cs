using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

internal sealed class ActorStateProcessor
{
    /// <summary>Extra beats of recovery while overheated.</summary>
    public const int OverheatRecoveryPenaltyBeats = 1;

    private const double Epsilon = 1e-6;

    private readonly GameCatalog _catalog;
    private readonly double _arenaWidth;
    private readonly double _arenaHeight;

    public ActorStateProcessor(GameCatalog catalog, double arenaWidth, double arenaHeight)
    {
        _catalog = catalog;
        _arenaWidth = arenaWidth;
        _arenaHeight = arenaHeight;
    }

    // ---- Step 1: passive timers -------------------------------------------------------------

    public void UpdatePassiveTimers(Combatant actor, int beat, List<CombatEvent> events)
    {
        var stats = actor.Stats;
        if (actor.Resources.AdvanceMoteTimer(stats.MoteGenerationIntervalBeats, stats.MaxMotes))
        {
            events.Add(new CombatEvent(
                beat,
                CombatEventType.MoteGenerated,
                actor.Id,
                $"{actor.Name} generated 1 Mote ({actor.Resources.CurrentMotes}/{stats.MaxMotes})."));
        }

        actor.Resources.CoolDown(stats.HeatDissipationPerBeat, stats.HeatCapacity);

        if (actor.EvadeBeatsRemaining > 0)
            actor.EvadeBeatsRemaining--;

        if (actor.HasActiveAegis)
        {
            actor.AegisBeatsRemaining--;
            if (actor.AegisBeatsRemaining <= 0)
                actor.ClearAegis();
        }

        if (actor.State == ActionState.Interrupted)
        {
            actor.InterruptedBeatsRemaining--;
            if (actor.InterruptedBeatsRemaining <= 0)
            {
                actor.InterruptedBeatsRemaining = 0;
                actor.State = ActionState.Idle;
            }
        }
    }

    // ---- Step 2: reactions ------------------------------------------------------------------

    public void ResolvePendingReaction(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<IncomingHit> hits,
        List<CombatEvent> events)
    {
        var cmd = actor.PendingReaction;
        if (cmd is null)
            return;

        actor.PendingReaction = null;

        if (!_catalog.Actions.TryGetValue(cmd.ActionId, out var def))
            return;

        var target = FindTarget(allCombatants, cmd.TargetActorId);
        var validation = ActionValidationRules.ValidateUseAction(actor, def, target, cmd.TargetPoint);
        if (!validation.IsValid)
        {
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name}'s {def.Name} failed: {validation.FailureReason}"));
            return;
        }

        // A reaction abandons any windup or movement in progress.
        if (actor.State is ActionState.Windup or ActionState.Moving)
            CancelCurrentAction(actor);

        var provider = actor.Loadout.FindOperationalProviderForAction(def.Id);
        var act = new ActiveAction(def, provider?.InstanceId, cmd.TargetActorId, cmd.TargetPoint);

        if (!TryCommitCosts(actor, act))
        {
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name}'s {def.Name} failed: costs could not be paid."));
            return;
        }

        actor.CurrentAction = act;
        events.Add(new CombatEvent(beat, CombatEventType.ActionStarted, actor.Id,
            $"{actor.Name} reacts with {def.Name}!", cmd.TargetActorId));

        ActionEffectResolver.ApplyExecutionEffect(actor, act, allCombatants, beat, _arenaWidth, _arenaHeight, hits, events);
        act.EffectApplied = true;

        // The declaring beat was the reaction's impact; recovery starts next beat.
        EnterRecovery(actor, act, beat);
    }

    // ---- Step 3: actions in motion ----------------------------------------------------------

    public void AdvanceAction(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<IncomingHit> hits,
        List<CombatEvent> events)
    {
        var act = actor.CurrentAction;
        if (act is null || actor.State is not (ActionState.Windup or ActionState.Executing or ActionState.Recovery))
            return;

        // Phases entered during this beat's reaction step start counting next beat.
        if (act.PhaseEnteredBeat == beat)
            return;

        act.PhaseBeatsRemaining--;
        if (act.PhaseBeatsRemaining > 0)
            return;

        switch (act.Phase)
        {
            case ActionState.Windup:
                EnterImpact(actor, act, beat, events);
                break;

            case ActionState.Executing:
                ResolveImpact(actor, act, allCombatants, beat, hits, events);
                break;

            case ActionState.Recovery:
                CompleteAction(actor, act);
                break;
        }
    }

    // ---- Step 5: movement -------------------------------------------------------------------

    public readonly record struct MovementResult(Vec2 Next, bool ReachesGoal, bool Cancel);

    public MovementResult ComputeMovement(Combatant actor, IReadOnlyList<Combatant> allCombatants)
    {
        var act = actor.CurrentAction;
        Vec2? destination;
        double stopDistance;

        if (act is not null && act.TargetActorId is not null)
        {
            var target = FindTarget(allCombatants, act.TargetActorId);
            if (target is null || target.IsDefeated)
                return new MovementResult(actor.Position, false, true);

            destination = target.Position;
            stopDistance = act.Definition.Range;
        }
        else if (act is not null && act.TargetPoint is not null)
        {
            destination = act.TargetPoint;
            stopDistance = act.Definition.Range;
        }
        else
        {
            destination = actor.MoveDestination;
            stopDistance = 0;
        }

        if (destination is null)
            return new MovementResult(actor.Position, true, act is not null);

        var delta = destination.Value - actor.Position;
        double travelNeeded = Math.Max(0, delta.Length - stopDistance);
        double step = Math.Min(actor.Stats.MoveSpeed, travelNeeded);
        var next = (actor.Position + delta.Normalized() * step).Clamp(0.5, _arenaWidth - 0.5, 0.5, _arenaHeight - 0.5);

        return new MovementResult(next, travelNeeded - step <= Epsilon, false);
    }

    public void AdvanceMovement(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events)
    {
        if (actor.State != ActionState.Moving)
            return;

        var result = ComputeMovement(actor, allCombatants);
        if (result.Cancel)
        {
            CancelCurrentAction(actor);
            return;
        }

        actor.Position = result.Next;
        if (!result.ReachesGoal)
            return;

        actor.MoveDestination = null;
        if (actor.CurrentAction is { } act)
        {
            BeginAction(actor, act, beat, events);
        }
        else
        {
            actor.State = ActionState.Idle;
        }
    }

    // ---- Step 6: buffered commands ----------------------------------------------------------

    public void ApplyBufferedCommands(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events)
    {
        if (actor.PendingCancel)
        {
            actor.PendingCancel = false;
            if (actor.State is ActionState.Windup or ActionState.Moving)
                CancelCurrentAction(actor);
        }

        var cmd = actor.QueuedCommand;
        if (cmd is null)
            return;

        switch (cmd)
        {
            case MoveCommand move:
                if (actor.State == ActionState.Windup)
                    CancelCurrentAction(actor);

                if (actor.State is not (ActionState.Idle or ActionState.Moving))
                    return; // stay queued until free

                actor.QueuedCommand = null;
                actor.CurrentAction = null;
                actor.MoveDestination = move.Destination.Clamp(0.5, _arenaWidth - 0.5, 0.5, _arenaHeight - 0.5);
                actor.State = ActionState.Moving;
                break;

            case UseActionCommand use when _catalog.Actions.TryGetValue(use.ActionId, out var def):
                if (actor.State is not (ActionState.Idle or ActionState.Moving))
                    return; // stay queued until free

                if (actor.GetCooldown(def.Id) > 0)
                    return; // stay queued until the cooldown expires

                actor.QueuedCommand = null;
                StartUseAction(actor, use, def, allCombatants, beat, events);
                break;

            default:
                actor.QueuedCommand = null;
                break;
        }
    }

    // ---- Shared transitions -----------------------------------------------------------------

    public void CancelCurrentAction(Combatant actor)
    {
        actor.CurrentAction = null;
        actor.MoveDestination = null;
        actor.Resources.ReleaseWit(Combatant.ActionWitReservationKey);
        actor.State = ActionState.Idle;
    }

    private void StartUseAction(
        Combatant actor,
        UseActionCommand cmd,
        ActionDefinition def,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events)
    {
        var target = FindTarget(allCombatants, cmd.TargetActorId);
        var validation = ActionValidationRules.ValidateUseAction(actor, def, target, cmd.TargetPoint);
        if (!validation.IsValid)
        {
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name}'s {def.Name} failed: {validation.FailureReason}"));
            return;
        }

        var provider = actor.Loadout.FindOperationalProviderForAction(def.Id);
        var active = new ActiveAction(def, provider?.InstanceId, cmd.TargetActorId, cmd.TargetPoint);
        actor.CurrentAction = active;

        if (ActionValidationRules.IsInRange(actor, def, target, cmd.TargetPoint))
        {
            BeginAction(actor, active, beat, events);
        }
        else
        {
            actor.State = ActionState.Moving;
            actor.MoveDestination = target?.Position ?? cmd.TargetPoint;
        }
    }

    /// <summary>Starts an action's windup (or impact, for zero-windup actions) in the next beat.</summary>
    private void BeginAction(Combatant actor, ActiveAction act, int beat, List<CombatEvent> events)
    {
        var def = act.Definition;
        if (def.WitReserve > 0)
            actor.Resources.ReserveWit(Combatant.ActionWitReservationKey, def.WitReserve);

        actor.MoveDestination = null;
        events.Add(new CombatEvent(
            beat,
            CombatEventType.ActionStarted,
            actor.Id,
            $"{actor.Name} winding up {def.Name}.",
            act.TargetActorId));

        if (def.WindupBeats > 0)
        {
            SetPhase(actor, act, ActionState.Windup, def.WindupBeats);
        }
        else
        {
            EnterImpact(actor, act, beat, events);
        }
    }

    private void EnterImpact(Combatant actor, ActiveAction act, int beat, List<CombatEvent> events)
    {
        if (!TryCommitCosts(actor, act))
        {
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name}'s {act.Definition.Name} fizzled: costs could not be paid."));
            CancelCurrentAction(actor);
            return;
        }

        SetPhase(actor, act, ActionState.Executing, Math.Max(1, act.Definition.ImpactBeats));
    }

    private void ResolveImpact(
        Combatant actor,
        ActiveAction act,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<IncomingHit> hits,
        List<CombatEvent> events)
    {
        ActionEffectResolver.ApplyExecutionEffect(actor, act, allCombatants, beat, _arenaWidth, _arenaHeight, hits, events);
        act.EffectApplied = true;
        actor.Resources.ReleaseWit(Combatant.ActionWitReservationKey);
        EnterRecovery(actor, act, beat: null);
    }

    private static void EnterRecovery(Combatant actor, ActiveAction act, int? beat)
    {
        int recovery = act.Definition.RecoveryBeats + (actor.Resources.IsOverheated ? OverheatRecoveryPenaltyBeats : 0);
        if (recovery <= 0)
        {
            CompleteAction(actor, act);
            return;
        }

        SetPhase(actor, act, ActionState.Recovery, recovery);
        if (beat is not null)
            act.PhaseEnteredBeat = beat.Value;
    }

    private static void CompleteAction(Combatant actor, ActiveAction act)
    {
        actor.CurrentAction = null;
        actor.State = ActionState.Idle;
        actor.SetCooldown(act.Definition.Id, act.Definition.CooldownBeats);
    }

    private static void SetPhase(Combatant actor, ActiveAction act, ActionState phase, int beats)
    {
        act.Phase = phase;
        act.PhaseTotalBeats = beats;
        act.PhaseBeatsRemaining = beats;
        actor.State = phase;
    }

    private static bool TryCommitCosts(Combatant actor, ActiveAction act)
    {
        var def = act.Definition;
        var provider = act.SourceEquipmentInstanceId is not null
            ? actor.Loadout.FindById(act.SourceEquipmentInstanceId)
            : null;

        if (provider is not null && provider.IsDisabled)
            return false;
        if (def.MoteCost > actor.Resources.CurrentMotes)
            return false;
        if (def.AmmoCost > 0 && (provider is null || provider.CurrentAmmo < def.AmmoCost))
            return false;

        actor.Resources.TrySpendMotes(def.MoteCost);
        if (def.AmmoCost > 0)
            provider!.TryConsumeAmmo(def.AmmoCost);

        act.CostsCommitted = true;
        return true;
    }

    private static Combatant? FindTarget(IReadOnlyList<Combatant> all, string? id) =>
        id is null ? null : all.FirstOrDefault(c => c.Id == id);
}
