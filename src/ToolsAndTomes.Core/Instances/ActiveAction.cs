using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Instances;

public sealed class ActiveAction
{
    public ActionDefinition Definition { get; }
    public string? SourceEquipmentInstanceId { get; }
    public string? TargetActorId { get; }
    public Vec2? TargetPoint { get; set; }
    public string? TargetComponentInstanceId { get; }

    public ActionState Phase { get; set; }
    public int PhaseTicksRemaining { get; set; }
    public int PhaseTotalTicks { get; set; }
    public bool EffectApplied { get; set; }
    public bool CostsCommitted { get; set; }

    public ActiveAction(
        ActionDefinition definition,
        string? sourceEquipmentInstanceId,
        string? targetActorId,
        Vec2? targetPoint,
        string? targetComponentInstanceId)
    {
        Definition = definition;
        SourceEquipmentInstanceId = sourceEquipmentInstanceId;
        TargetActorId = targetActorId;
        TargetPoint = targetPoint;
        TargetComponentInstanceId = targetComponentInstanceId;
        Phase = ActionState.Windup;
    }

    public int TicksUntilExecution()
    {
        return Phase switch
        {
            ActionState.Windup => PhaseTicksRemaining,
            ActionState.Executing => 0,
            _ => -1
        };
    }
}
