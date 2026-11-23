namespace ToolsAndTomes.Core.Instances;

/// <summary>
/// Movement carried out by a locomotion component. Either walks to a fixed destination, or follows a
/// target (combatant or point) until within <see cref="StopDistance"/> — used when an action approaches into range.
/// </summary>
public sealed record MovementOrder
{
    public required string ProviderInstanceId { get; init; }
    public Vec2? Destination { get; init; }
    public string? FollowActorId { get; init; }
    public double StopDistance { get; init; }

    /// <summary>The approaching action this movement serves, if any.</summary>
    public int? ForActionId { get; init; }
}
