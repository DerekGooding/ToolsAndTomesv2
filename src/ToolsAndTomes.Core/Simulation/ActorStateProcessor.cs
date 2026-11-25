using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

/// <summary>
/// Per-combatant beat processing. A Crest may run several actions at once (one per component),
/// each holding its own Wit reservation, plus one movement order carried out by a locomotion component.
/// </summary>
internal sealed class ActorStateProcessor
{
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

        actor.Resources.CoolDown(stats.HeatDissipationPerBeat, stats.HeatCapacity, actor.Rules.OverheatClearPercent);

        if (actor.EvadeBeatsRemaining > 0)
            actor.EvadeBeatsRemaining--;

        if (actor.HasActiveAegis)
        {
            actor.AegisBeatsRemaining--;
            if (actor.AegisBeatsRemaining <= 0)
                actor.ClearAegis();
        }

        actor.TickLockouts();
        actor.TickSpellStatuses();
    }

    // ---- Step 2: reactions ------------------------------------------------------------------

    /// <summary>
    /// If the Crest has not declared a reaction this beat, its Workshop reaction rules are checked in order;
    /// the first rule that matches an incoming impact and is legal is declared.
    /// </summary>
    public void EvaluateReactionRules(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events)
    {
        if (actor.PendingReactions.Count > 0 || actor.Loadout.ReactionRules.Count == 0)
            return;

        var impacts = ImpactRules.GetIncomingImpacts(actor, allCombatants);
        if (impacts.Count == 0)
            return;

        foreach (var rule in actor.Loadout.ReactionRules)
        {
            if (!_catalog.Actions.TryGetValue(rule.ActionId, out var def) || !def.IsReactive)
                continue;

            var trigger = impacts.FirstOrDefault(i =>
                i.ExpectedDamage >= rule.MinIncomingDamage &&
                (rule.Telegraph is null || i.Action.Definition.Telegraph == rule.Telegraph));
            if (trigger is null)
                continue;

            var target = def.Targeting == TargetingType.EnemyUnit ? trigger.Attacker : null;
            var cmd = new UseActionCommand(actor.Id, def.Id, target?.Id);
            if (!ActionValidationRules.ValidateUseAction(actor, def, target, null, allCombatants).IsValid)
                continue;

            actor.AddPendingReaction(cmd);
            events.Add(new CombatEvent(beat, CombatEventType.ReactionRuleTriggered, actor.Id,
                $"{actor.Name}'s reaction rule fires: {def.Name} vs {trigger.Attacker.Name}'s {trigger.Action.Definition.Name}.",
                trigger.Attacker.Id));
            return;
        }
    }

    public void ResolvePendingReactions(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<IncomingHit> hits,
        List<CombatEvent> events)
    {
        // Reactions resolve instantly, but within this step each holds its Wit so the next reaction sees what is left.
        var heldThisStep = new List<string>();
        foreach (var cmd in actor.TakePendingReactions())
        {
            if (!_catalog.Actions.TryGetValue(cmd.ActionId, out var def))
                continue;

            var target = FindTarget(allCombatants, cmd.TargetActorId);
            var validation = ActionValidationRules.ValidateUseAction(actor, def, target, cmd.TargetPoint, allCombatants);
            var provider = actor.FindFreeProvider(def.Id);
            var (shapingCheck, plan) = ShapingRules.Validate(actor, def, cmd, _catalog);
            if (!validation.IsValid || !shapingCheck.IsValid || provider is null)
            {
                Reject(actor, def, validation.FailureReason ?? shapingCheck.FailureReason ?? "its component is busy", beat, events);
                continue;
            }

            var act = actor.AddAction(def, provider.InstanceId, cmd.TargetActorId, cmd.TargetPoint);
            act.OwnComponentInstanceId = cmd.OwnComponentInstanceId;
            act.ShapingPlan = plan;
            if (def.WitReserve > 0 && def.AegisBlocks == 0) // Aegis reserves its own Wit while it lasts
            {
                // Held under its own key: a zero-recovery reaction completes (and releases its action key) at once.
                actor.Resources.ReserveWit($"reaction_hold_{act.Id}", def.WitReserve);
                heldThisStep.Add($"reaction_hold_{act.Id}");
            }

            if (!TryCommitCosts(actor, act))
            {
                actor.RemoveAction(act);
                actor.Resources.ReleaseWit($"reaction_hold_{act.Id}");
                Reject(actor, def, "costs could not be paid", beat, events);
                continue;
            }

            events.Add(new CombatEvent(beat, CombatEventType.ActionStarted, actor.Id,
                $"{actor.Name} reacts with {def.Name}!", cmd.TargetActorId, ActionId: def.Id));

            ActionEffectResolver.ApplyExecutionEffect(actor, act, allCombatants, beat, _arenaWidth, _arenaHeight, hits, events, _catalog);
            act.EffectApplied = true;

            // The declaring beat was the reaction's impact; recovery starts next beat.
            EnterRecovery(actor, act, enteredThisBeat: beat);
        }

        foreach (var key in heldThisStep)
            actor.Resources.ReleaseWit(key);
    }

    // ---- Step 3: actions in motion ----------------------------------------------------------

    public void AdvanceActions(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<IncomingHit> hits,
        List<CombatEvent> events)
    {
        foreach (var act in actor.ActionsInMotion.ToList())
        {
            if (act.Phase is not (ActionState.Windup or ActionState.Executing or ActionState.Recovery))
                continue;

            // Phases entered during this beat's reaction step start counting next beat.
            if (act.PhaseEnteredBeat == beat)
                continue;

            act.PhaseBeatsRemaining--;
            if (act.PhaseBeatsRemaining > 0)
                continue;

            switch (act.Phase)
            {
                case ActionState.Windup:
                    EnterImpact(actor, act, beat, events);
                    break;

                case ActionState.Executing:
                    ActionEffectResolver.ApplyExecutionEffect(actor, act, allCombatants, beat, _arenaWidth, _arenaHeight, hits, events, _catalog);
                    act.EffectApplied = true;
                    actor.Resources.ReleaseWit(act.WitReservationKey);
                    EnterRecovery(actor, act, enteredThisBeat: null);
                    break;

                case ActionState.Recovery:
                    CompleteAction(actor, act);
                    break;
            }
        }
    }

    // ---- Step 5: movement -------------------------------------------------------------------

    public readonly record struct MovementResult(Vec2 Next, bool ReachesGoal, bool Cancel);

    public MovementResult ComputeMovement(Combatant actor, IReadOnlyList<Combatant> allCombatants)
    {
        var order = actor.Movement;
        if (order is null)
            return new MovementResult(actor.Position, true, false);

        Vec2? destination = order.Destination;
        if (order.FollowActorId is not null)
        {
            var target = FindTarget(allCombatants, order.FollowActorId);
            if (target is null || target.IsDefeated)
                return new MovementResult(actor.Position, false, true);
            destination = target.Position;
        }

        if (destination is null)
            return new MovementResult(actor.Position, true, false);

        var delta = destination.Value - actor.Position;
        double travelNeeded = Math.Max(0, delta.Length - order.StopDistance);
        double step = Math.Min(actor.EffectiveMoveSpeed, travelNeeded);
        var next = ClampToArena(actor.Position + delta.Normalized() * step);

        return new MovementResult(next, travelNeeded - step <= Epsilon, false);
    }

    public void AdvanceMovement(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events)
    {
        if (actor.Movement is { } order)
        {
            var provider = actor.Loadout.FindById(order.ProviderInstanceId);
            var result = ComputeMovement(actor, allCombatants);

            if (provider is null || !provider.IsOperational || result.Cancel)
            {
                actor.StopMovement();
            }
            else
            {
                actor.Position = result.Next;
                if (result.ReachesGoal && order.ForActionId is null)
                    actor.StopMovement();
            }
        }

        // Approaching actions start their windup once in range.
        foreach (var act in actor.ActionsInMotion.Where(a => a.Phase == ActionState.Moving).ToList())
        {
            var target = FindTarget(allCombatants, act.TargetActorId);
            if (act.TargetActorId is not null && (target is null || target.IsDefeated))
            {
                actor.RemoveAction(act);
                continue;
            }

            if (ActionValidationRules.IsInRange(actor, act.Definition, target, act.TargetPoint))
            {
                BeginAction(actor, act, beat, events);
            }
            else if (actor.Movement is null)
            {
                // Movement stopped (legs lost, or never available): the approach is abandoned.
                actor.RemoveAction(act);
                Reject(actor, act.Definition, "target out of range", beat, events);
            }
        }

        // An approach walk ends once nothing is approaching any more.
        if (actor.Movement is { ForActionId: not null } && !actor.ActionsInMotion.Any(a => a.Phase == ActionState.Moving))
            actor.StopMovement();
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
            CancelUncommitted(actor);
        }

        foreach (var cmd in actor.QueuedCommands.ToList())
        {
            switch (cmd)
            {
                case MoveCommand move:
                    actor.RemoveQueuedCommand(cmd);
                    StartMovement(actor, move, beat, events);
                    break;

                case UseActionCommand use when _catalog.Actions.TryGetValue(use.ActionId, out var def):
                    if (actor.GetCooldown(def.Id) > 0)
                        continue; // stays queued until the cooldown expires

                    var provider = actor.FindFreeProvider(def.Id);
                    if (provider is null)
                    {
                        if (actor.Loadout.FindOperationalProviderForAction(def.Id) is null)
                        {
                            actor.RemoveQueuedCommand(cmd);
                            Reject(actor, def, "no operational component provides it", beat, events);
                        }
                        continue; // component busy: stays queued
                    }

                    actor.RemoveQueuedCommand(cmd);
                    StartUseAction(actor, use, def, provider, allCombatants, beat, events);
                    break;

                default:
                    actor.RemoveQueuedCommand(cmd);
                    break;
            }
        }
    }

    // ---- Shared transitions -----------------------------------------------------------------

    /// <summary>Cancels movement and every action that has not yet committed (approaching or winding up).</summary>
    public static void CancelUncommitted(Combatant actor)
    {
        foreach (var act in actor.ActionsInMotion.Where(a => a.IsCancelable).ToList())
            actor.RemoveAction(act);

        actor.StopMovement();
    }

    private void StartMovement(Combatant actor, MoveCommand move, int beat, List<CombatEvent> events)
    {
        var legs = actor.FindLocomotionProvider();
        if (legs is null)
        {
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name} cannot move: no operational locomotion."));
            return;
        }

        if (!CanAffordMovement(actor, legs))
        {
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name} cannot move: insufficient Wit for {legs.Definition.Name}."));
            return;
        }

        // A plain move redirects the legs: attacks that were approaching a target are abandoned.
        AbandonApproaches(actor, keepActionId: null, "the legs were ordered elsewhere", beat, events);
        ReserveMovementWit(actor, legs);
        actor.Movement = new MovementOrder
        {
            ProviderInstanceId = legs.InstanceId,
            Destination = ClampToArena(move.Destination)
        };
    }

    /// <summary>
    /// Whether a new approach heads for the same place as the current one: the same combatant, the same point, or a
    /// point on (within 1 unit of) the combatant being followed, e.g. a Mortar aimed at the enemy a blade is chasing.
    /// </summary>
    private static bool IsSameApproachTarget(MovementOrder current, Combatant? newTarget, Vec2? newPoint, IReadOnlyList<Combatant> all)
    {
        Vec2? currentSpot = current.FollowActorId is { } followId ? FindTarget(all, followId)?.Position : current.Destination;
        Vec2? newSpot = newTarget?.Position ?? newPoint;

        if (newTarget is not null && current.FollowActorId == newTarget.Id)
            return true;
        return currentSpot is { } a && newSpot is { } b && a.DistanceTo(b) <= 1.0;
    }

    /// <summary>Removes approaching (not yet winding up) actions, except <paramref name="keepActionId"/>, with a notice.</summary>
    private static void AbandonApproaches(Combatant actor, int? keepActionId, string reason, int beat, List<CombatEvent> events)
    {
        foreach (var approaching in actor.ActionsInMotion.Where(a => a.Phase == ActionState.Moving && a.Id != keepActionId).ToList())
        {
            actor.RemoveAction(approaching);
            Reject(actor, approaching.Definition, $"approach abandoned: {reason}", beat, events);
        }
    }

    private void StartUseAction(
        Combatant actor,
        UseActionCommand cmd,
        ActionDefinition def,
        EquipmentInstance provider,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events)
    {
        var target = FindTarget(allCombatants, cmd.TargetActorId);
        var validation = ActionValidationRules.ValidateUseAction(actor, def, target, cmd.TargetPoint);
        if (!validation.IsValid)
        {
            Reject(actor, def, validation.FailureReason!, beat, events);
            return;
        }

        var (shapingCheck, plan) = ShapingRules.Validate(actor, def, cmd, _catalog);
        if (!shapingCheck.IsValid)
        {
            Reject(actor, def, shapingCheck.FailureReason!, beat, events);
            return;
        }

        bool inRange = ActionValidationRules.IsInRange(actor, def, target, cmd.TargetPoint);
        var legs = inRange ? null : actor.FindLocomotionProvider();
        if (!inRange && legs is null)
        {
            Reject(actor, def, "target out of range and the Crest cannot move", beat, events);
            return;
        }

        var act = actor.AddAction(def, provider.InstanceId, cmd.TargetActorId, cmd.TargetPoint);
        act.OwnComponentInstanceId = cmd.OwnComponentInstanceId;
        act.ShapingPlan = plan;

        if (inRange)
        {
            BeginAction(actor, act, beat, events);
            return;
        }

        // Another attack is already approaching the same target: share the walk, stopping at the shorter range.
        if (actor.Movement is { ForActionId: not null } shared && IsSameApproachTarget(shared, target, cmd.TargetPoint, allCombatants))
        {
            act.Phase = ActionState.Moving;
            actor.Movement = shared with { StopDistance = Math.Min(shared.StopDistance, def.Range) };
            return;
        }

        if (!CanAffordMovement(actor, legs!))
        {
            actor.RemoveAction(act);
            Reject(actor, def, $"insufficient Wit to walk with {legs!.Definition.Name}", beat, events);
            return;
        }

        AbandonApproaches(actor, keepActionId: act.Id, $"the legs were redirected toward {def.Name}'s target", beat, events);
        if (actor.Movement is { ForActionId: null })
        {
            events.Add(new CombatEvent(beat, CombatEventType.OrderReplaced, actor.Id,
                $"{actor.Name}'s move order was replaced: approaching for {def.Name}."));
        }

        ReserveMovementWit(actor, legs!);
        act.Phase = ActionState.Moving;
        actor.Movement = new MovementOrder
        {
            ProviderInstanceId = legs!.InstanceId,
            FollowActorId = cmd.TargetActorId,
            Destination = cmd.TargetActorId is null ? cmd.TargetPoint : null,
            StopDistance = def.Range,
            ForActionId = act.Id
        };
    }

    /// <summary>Starts an action's windup (or impact, for zero-windup actions) in the next beat.</summary>
    private static void BeginAction(Combatant actor, ActiveAction act, int beat, List<CombatEvent> events)
    {
        var def = act.Definition;
        if (def.WitReserve > 0)
        {
            if (actor.Stats.AvailableWit(actor.Resources.ReservedWit) < def.WitReserve)
            {
                actor.RemoveAction(act);
                Reject(actor, def, $"insufficient available Wit (needs {def.WitReserve})", beat, events);
                return;
            }

            actor.Resources.ReserveWit(act.WitReservationKey, def.WitReserve);
        }

        events.Add(new CombatEvent(
            beat,
            CombatEventType.ActionStarted,
            actor.Id,
            $"{actor.Name} winding up {def.Name}.",
            act.TargetActorId,
            ActionId: def.Id));

        if (def.WindupBeats > 0)
        {
            // Leaden Air: the next windup is slower.
            int delay = actor.PendingWindupDelay;
            actor.PendingWindupDelay = 0;
            SetPhase(act, ActionState.Windup, def.WindupBeats + delay);
        }
        else
            EnterImpact(actor, act, beat, events);
    }

    private static void EnterImpact(Combatant actor, ActiveAction act, int beat, List<CombatEvent> events)
    {
        if (!TryCommitCosts(actor, act))
        {
            actor.RemoveAction(act);
            events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                $"{actor.Name}'s {act.Definition.Name} fizzled: costs could not be paid."));
            return;
        }

        SetPhase(act, ActionState.Executing, Math.Max(1, act.Definition.ImpactBeats));
    }

    private static void EnterRecovery(Combatant actor, ActiveAction act, int? enteredThisBeat)
    {
        int recovery = act.Definition.RecoveryBeats + (actor.Resources.IsOverheated ? actor.Rules.OverheatRecoveryPenaltyBeats : 0);
        if (recovery <= 0)
        {
            CompleteAction(actor, act);
            return;
        }

        SetPhase(act, ActionState.Recovery, recovery);
        if (enteredThisBeat is not null)
            act.PhaseEnteredBeat = enteredThisBeat.Value;
    }

    private static void CompleteAction(Combatant actor, ActiveAction act)
    {
        actor.RemoveAction(act);
        actor.SetCooldown(act.Definition.Id, act.Definition.CooldownBeats);
    }

    private static void SetPhase(ActiveAction act, ActionState phase, int beats)
    {
        act.Phase = phase;
        act.PhaseTotalBeats = beats;
        act.PhaseBeatsRemaining = beats;
    }

    /// <summary>Whether a new walk is affordable, counting the current walk's Wit as freed (it would be replaced).</summary>
    private static bool CanAffordMovement(Combatant actor, EquipmentInstance legs)
    {
        int cost = legs.Definition.MovementWitCost;
        if (cost <= 0)
            return true;

        int current = actor.Resources.GetReservation(Combatant.MovementWitReservationKey);
        return actor.Stats.AvailableWit(actor.Resources.ReservedWit - current) >= cost;
    }

    /// <summary>Replaces the walk's Wit reservation (call only after <see cref="CanAffordMovement"/>).</summary>
    private static void ReserveMovementWit(Combatant actor, EquipmentInstance legs)
    {
        actor.Resources.ReleaseWit(Combatant.MovementWitReservationKey);
        actor.Resources.ReserveWit(Combatant.MovementWitReservationKey, legs.Definition.MovementWitCost);
    }

    private static bool TryCommitCosts(Combatant actor, ActiveAction act)
    {
        var def = act.Definition;
        var provider = actor.Loadout.FindById(act.SourceEquipmentInstanceId);

        if (provider is null || provider.IsDisabled)
            return false;
        if (def.MoteCost > actor.Resources.CurrentMotes)
            return false;
        if (def.AmmoCost > 0 && provider.CurrentAmmo < def.AmmoCost)
            return false;
        if (def.ConsumableItemId is not null && actor.Loadout.GetConsumableCount(def.ConsumableItemId) <= 0)
            return false;

        int fastIron = act.ShapingPlan?.FastIronCost ?? 0;
        if (actor.TotalFastIron < fastIron)
            return false;

        actor.Resources.TrySpendMotes(def.MoteCost);
        if (def.AmmoCost > 0)
            provider.TryConsumeAmmo(def.AmmoCost);
        if (def.ConsumableItemId is not null)
            actor.Loadout.TryTakeConsumable(def.ConsumableItemId);
        actor.TrySpendFastIron(fastIron);

        act.CostsCommitted = true;
        return true;
    }

    private Vec2 ClampToArena(Vec2 p) => p.Clamp(0.5, _arenaWidth - 0.5, 0.5, _arenaHeight - 0.5);

    private static void Reject(Combatant actor, ActionDefinition def, string reason, int beat, List<CombatEvent> events) =>
        events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
            $"{actor.Name}'s {def.Name} failed: {reason}"));

    private static Combatant? FindTarget(IReadOnlyList<Combatant> all, string? id) =>
        id is null ? null : all.FirstOrDefault(c => c.Id == id);
}
