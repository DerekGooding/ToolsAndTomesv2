using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

/// <summary>
/// One action running on one component. A Crest may run several at once (one per component),
/// as long as it has the Wit to commit to all of them.
/// </summary>
public sealed class ActiveAction
{
    public int Id { get; }
    public ActionDefinition Definition { get; }

    /// <summary>The component this action runs on. Each component runs at most one action at a time.</summary>
    public string SourceEquipmentInstanceId { get; }
    public string? TargetActorId { get; }
    public Vec2? TargetPoint { get; set; }

    /// <summary>
    /// <see cref="ActionState.Moving"/> while approaching into range, then Windup → Executing (impact window) → Recovery.
    /// </summary>
    public ActionState Phase { get; set; }

    /// <summary>Beats left in the current phase, including the beat in progress.</summary>
    public int PhaseBeatsRemaining { get; set; }
    public int PhaseTotalBeats { get; set; }
    public bool EffectApplied { get; set; }

    /// <summary>Beat during which the current phase was entered outside the normal advance step (e.g. a reaction), or -1.</summary>
    public int PhaseEnteredBeat { get; set; } = -1;
    public bool CostsCommitted { get; set; }

    public string WitReservationKey => $"action_{Id}";

    /// <summary>Approaching or winding up: nothing committed yet, so it may be freely canceled.</summary>
    public bool IsCancelable => Phase is ActionState.Moving or ActionState.Windup;

    /// <summary>Windup or impact window: the phases in which an action can be disrupted or interrupted.</summary>
    public bool IsInterruptiblePhase => Phase is ActionState.Windup or ActionState.Executing;

    public ActiveAction(
        int id,
        ActionDefinition definition,
        string sourceEquipmentInstanceId,
        string? targetActorId,
        Vec2? targetPoint)
    {
        Id = id;
        Definition = definition;
        SourceEquipmentInstanceId = sourceEquipmentInstanceId;
        TargetActorId = targetActorId;
        TargetPoint = targetPoint;
        Phase = ActionState.Moving;
    }

    /// <summary>
    /// Beats until the impact resolves, counting the beat in progress (1 = resolves at the end of this beat).
    /// Returns -1 when not yet winding up or once the impact has resolved.
    /// </summary>
    public int BeatsUntilImpact()
    {
        return Phase switch
        {
            ActionState.Windup => PhaseBeatsRemaining + Math.Max(1, Definition.ImpactBeats),
            ActionState.Executing => PhaseBeatsRemaining,
            _ => -1
        };
    }
}
