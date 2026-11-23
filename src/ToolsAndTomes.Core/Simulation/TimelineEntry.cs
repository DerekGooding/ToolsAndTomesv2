using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public sealed record TimelineEntry(
    string ActorId,
    string ActorName,
    Faction Faction,
    ActionState State,
    string ActionName,
    TelegraphKind? Telegraph,
    string? TargetActorId,
    Vec2? TargetPoint,
    double AreaRadius,
    int TicksRemaining,
    int PhaseTotalTicks,
    int TicksUntilImpact,
    bool CanBeInterrupted);
