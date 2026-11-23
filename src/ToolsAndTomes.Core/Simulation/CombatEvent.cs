using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public enum CombatEventType
{
    ActionStarted,
    ActionStateChanged,
    ActionExecuted,
    ActionInterrupted,
    DamageRolled,
    DamageBlocked,
    ComponentDamaged,
    ComponentDisabled,
    CombatantDisplaced,
    CombatantBlinked,
    AegisActivated,
    AegisBlocked,
    MoteGenerated,
    Overheated,
    CombatantDefeated,
    CommandRejected,
    FastIronProduced
}

public sealed record CombatEvent(
    int Beat,
    CombatEventType Type,
    string ActorId,
    string Message,
    string? TargetId = null,
    int? Value = null,
    InterruptionTier Interruption = InterruptionTier.None,
    Vec2? Position = null);
