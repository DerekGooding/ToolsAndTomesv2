using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

public sealed class ActiveAction
{
    public ActionDefinition Definition { get; }
    public string? SourceEquipmentInstanceId { get; }
    public string? TargetActorId { get; }
    public Vec2? TargetPoint { get; set; }

    public ActionState Phase { get; set; }

    /// <summary>Beats left in the current phase, including the beat in progress.</summary>
    public int PhaseBeatsRemaining { get; set; }
    public int PhaseTotalBeats { get; set; }
    public bool EffectApplied { get; set; }

    /// <summary>Beat during which the current phase was entered outside the normal advance step (e.g. a reaction), or -1.</summary>
    public int PhaseEnteredBeat { get; set; } = -1;
    public bool CostsCommitted { get; set; }

    public ActiveAction(
        ActionDefinition definition,
        string? sourceEquipmentInstanceId,
        string? targetActorId,
        Vec2? targetPoint)
    {
        Definition = definition;
        SourceEquipmentInstanceId = sourceEquipmentInstanceId;
        TargetActorId = targetActorId;
        TargetPoint = targetPoint;
        Phase = ActionState.Windup;
    }

    /// <summary>
    /// Beats until the impact resolves, counting the beat in progress (1 = resolves at the end of this beat).
    /// Returns -1 once the impact has resolved.
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
