using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

internal sealed class ActorStateProcessor
{
    private readonly GameCatalog _catalog;
    private readonly double _arenaWidth;
    private readonly double _arenaHeight;

    public ActorStateProcessor(GameCatalog catalog, double arenaWidth, double arenaHeight)
    {
        _catalog = catalog;
        _arenaWidth = arenaWidth;
        _arenaHeight = arenaHeight;
    }

    public void UpdatePassiveTimers(Combatant actor, int tick, List<CombatEvent> events)
    {
        actor.TickCooldowns();

        var stats = actor.Stats;
        if (actor.Resources.AdvanceMoteTimer(stats.MoteGenerationIntervalTicks, stats.MaxMotes))
        {
            events.Add(new CombatEvent(
                tick,
                CombatEventType.MoteGenerated,
                actor.Id,
                $"{actor.Name} generated 1 Mote ({actor.Resources.CurrentMotes}/{stats.MaxMotes})."));
        }

        actor.Resources.CoolDown(stats.HeatDissipationPerSecond / ActionCatalog.TicksPerSecond, stats.HeatCapacity);

        if (actor.EvadeTicksRemaining > 0)
            actor.EvadeTicksRemaining--;

        if (actor.HasActiveAegis)
        {
            actor.AegisTicksRemaining--;
            if (actor.AegisTicksRemaining <= 0)
                actor.ClearAegis();
        }
    }

    public void AdvanceActorState(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        List<IncomingHit> incomingHits,
        List<CombatEvent> events)
    {
        if (actor.State == ActionState.Interrupted)
        {
            actor.InterruptedTicksRemaining--;
            if (actor.InterruptedTicksRemaining <= 0)
            {
                actor.State = ActionState.Idle;
                TryConsumeQueuedCommand(actor, allCombatants, tick, events);
            }
            return;
        }

        if (actor.State == ActionState.Moving)
        {
            AdvanceMovement(actor, allCombatants, tick, events);
            return;
        }

        if (actor.CurrentAction is null)
        {
            TryConsumeQueuedCommand(actor, allCombatants, tick, events);
            return;
        }

        var act = actor.CurrentAction;
        act.PhaseTicksRemaining--;

        if (act.Phase == ActionState.Windup && act.PhaseTicksRemaining <= 0)
        {
            EnterExecuting(actor, act, allCombatants, tick, incomingHits, events);
        }
        else if (act.Phase == ActionState.Executing && act.PhaseTicksRemaining <= 0)
        {
            EnterRecovery(actor, act);
        }
        else if (act.Phase == ActionState.Recovery && act.PhaseTicksRemaining <= 0)
        {
            actor.CurrentAction = null;
            actor.State = ActionState.Idle;
            TryConsumeQueuedCommand(actor, allCombatants, tick, events);
        }
    }

    public void ApplyCommandImmediately(
        Combatant actor,
        CombatCommand command,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        List<CombatEvent> events)
    {
        switch (command)
        {
            case MoveCommand move:
                actor.CurrentAction = null;
                actor.MoveDestination = move.Destination.Clamp(0.5, _arenaWidth - 0.5, 0.5, _arenaHeight - 0.5);
                actor.State = ActionState.Moving;
                break;

            case UseActionCommand use when _catalog.Actions.TryGetValue(use.ActionId, out var def):
                StartUseAction(actor, use, def, allCombatants, tick, events);
                break;
        }
    }

    public void StartUseAction(
        Combatant actor,
        UseActionCommand cmd,
        ActionDefinition def,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        List<CombatEvent> events)
    {
        var target = cmd.TargetActorId is not null ? allCombatants.FirstOrDefault(c => c.Id == cmd.TargetActorId) : null;
        if (!ActionValidationRules.ValidateUseAction(actor, def, target, cmd.TargetPoint).IsValid)
            return;

        var provider = actor.Loadout.FindOperationalProviderForAction(def.Id);
        var active = new ActiveAction(def, provider?.InstanceId, cmd.TargetActorId, cmd.TargetPoint, cmd.TargetComponentInstanceId);
        actor.CurrentAction = active;

        if (ActionValidationRules.IsInRange(actor, def, target, cmd.TargetPoint))
        {
            BeginWindup(actor, active, tick, events);
        }
        else
        {
            actor.State = ActionState.Moving;
            actor.MoveDestination = target?.Position ?? cmd.TargetPoint;
        }
    }

    public void CancelCurrentAction(Combatant actor)
    {
        actor.CurrentAction = null;
        actor.MoveDestination = null;
        actor.Resources.ReleaseWit(Combatant.ActionWitReservationKey);
        actor.State = ActionState.Idle;
    }

    private void AdvanceMovement(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        List<CombatEvent> events)
    {
        var act = actor.CurrentAction;
        Vec2? destination = actor.MoveDestination;

        if (act is not null && act.TargetActorId is not null)
        {
            var target = allCombatants.FirstOrDefault(c => c.Id == act.TargetActorId);
            if (target is null || target.IsDefeated)
            {
                CancelCurrentAction(actor);
                return;
            }

            destination = target.Position;
            if (ActionValidationRules.IsInRange(actor, act.Definition, target, act.TargetPoint))
            {
                BeginWindup(actor, act, tick, events);
                return;
            }
        }
        else if (act is not null && act.TargetPoint is not null)
        {
            destination = act.TargetPoint;
            if (ActionValidationRules.IsInRange(actor, act.Definition, null, act.TargetPoint))
            {
                BeginWindup(actor, act, tick, events);
                return;
            }
        }

        if (destination is null)
        {
            actor.State = ActionState.Idle;
            return;
        }

        double step = actor.Stats.MoveSpeed / ActionCatalog.TicksPerSecond;
        var delta = destination.Value - actor.Position;
        if (delta.Length <= step)
        {
            actor.Position = destination.Value.Clamp(0.5, _arenaWidth - 0.5, 0.5, _arenaHeight - 0.5);
            actor.MoveDestination = null;

            if (act is not null)
                BeginWindup(actor, act, tick, events);
            else
            {
                actor.State = ActionState.Idle;
                TryConsumeQueuedCommand(actor, allCombatants, tick, events);
            }
        }
        else
        {
            actor.Position = (actor.Position + delta.Normalized() * step)
                .Clamp(0.5, _arenaWidth - 0.5, 0.5, _arenaHeight - 0.5);
        }
    }

    private void BeginWindup(Combatant actor, ActiveAction act, int tick, List<CombatEvent> events)
    {
        var def = act.Definition;
        if (def.WitReserve > 0 && def.AegisBlocks == 0)
            actor.Resources.ReserveWit(Combatant.ActionWitReservationKey, def.WitReserve);

        act.Phase = ActionState.Windup;
        act.PhaseTotalTicks = Math.Max(1, def.WindupTicks);
        act.PhaseTicksRemaining = act.PhaseTotalTicks;
        actor.State = ActionState.Windup;
        actor.MoveDestination = null;

        events.Add(new CombatEvent(
            tick,
            CombatEventType.ActionStarted,
            actor.Id,
            $"{actor.Name} winding up {def.Name}.",
            act.TargetActorId));
    }

    private void EnterExecuting(
        Combatant actor,
        ActiveAction act,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        List<IncomingHit> incomingHits,
        List<CombatEvent> events)
    {
        var def = act.Definition;
        var provider = act.SourceEquipmentInstanceId is not null
            ? actor.Loadout.FindById(act.SourceEquipmentInstanceId)
            : null;

        if ((provider is not null && provider.IsDisabled) ||
            !actor.Resources.TrySpendMotes(def.MoteCost) ||
            (def.AmmoCost > 0 && (provider is null || !provider.TryConsumeAmmo(def.AmmoCost))))
        {
            CancelCurrentAction(actor);
            return;
        }

        act.CostsCommitted = true;
        act.Phase = ActionState.Executing;
        act.PhaseTotalTicks = Math.Max(1, def.ExecutionTicks);
        act.PhaseTicksRemaining = act.PhaseTotalTicks;
        actor.State = ActionState.Executing;
        actor.SetCooldown(def.Id, def.CooldownTicks);

        ActionEffectResolver.ApplyExecutionEffect(
            actor, act, allCombatants, tick, _arenaWidth, _arenaHeight, incomingHits, events);
        act.EffectApplied = true;
    }

    private static void EnterRecovery(Combatant actor, ActiveAction act)
    {
        actor.Resources.ReleaseWit(Combatant.ActionWitReservationKey);
        int extraHeatRecovery = actor.Resources.IsOverheated ? 10 : 0;

        act.Phase = ActionState.Recovery;
        act.PhaseTotalTicks = Math.Max(1, act.Definition.RecoveryTicks + extraHeatRecovery);
        act.PhaseTicksRemaining = act.PhaseTotalTicks;
        actor.State = ActionState.Recovery;
    }

    private void TryConsumeQueuedCommand(
        Combatant actor,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        List<CombatEvent> events)
    {
        if (actor.QueuedCommand is null)
            return;

        var next = actor.QueuedCommand;
        actor.QueuedCommand = null;
        ApplyCommandImmediately(actor, next, allCombatants, tick, events);
    }
}
