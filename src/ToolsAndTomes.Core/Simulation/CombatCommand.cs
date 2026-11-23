using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public abstract record CombatCommand(string ActorId);

public sealed record MoveCommand(string ActorId, Vec2 Destination) : CombatCommand(ActorId);

public sealed record UseActionCommand(
    string ActorId,
    string ActionId,
    string? TargetActorId = null,
    Vec2? TargetPoint = null,
    string? TargetComponentInstanceId = null) : CombatCommand(ActorId);

public sealed record CancelActionCommand(string ActorId) : CombatCommand(ActorId);
