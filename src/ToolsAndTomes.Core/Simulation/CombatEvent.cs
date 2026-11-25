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
    ReactionRuleTriggered,
    WitCollapse,
    OrderReplaced,
    FastIronProduced,
    FieldShaped,
    AmmoRefilled,
    ObstacleDamaged,
    ObstacleDestroyed,
    ObstacleRaised,

    /// <summary>A Crest was pushed or pulled into a hard obstacle (collision damage follows as ComponentDamaged).</summary>
    Slammed
}

public sealed record CombatEvent(
    int Beat,
    CombatEventType Type,
    string ActorId,
    string Message,
    string? TargetId = null,
    int? Value = null,
    InterruptionTier Interruption = InterruptionTier.None,
    Vec2? Position = null,
    string? ActionId = null,
    string? ComponentInstanceId = null,
    string? ObstacleId = null);
