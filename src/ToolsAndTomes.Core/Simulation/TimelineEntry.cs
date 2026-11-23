using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

/// <summary>
/// Snapshot of one combatant's current activity. Beat counts include the beat in progress.
/// </summary>
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
    int BeatsRemaining,
    int PhaseTotalBeats,
    int BeatsUntilImpact,
    bool CanBeInterrupted);
