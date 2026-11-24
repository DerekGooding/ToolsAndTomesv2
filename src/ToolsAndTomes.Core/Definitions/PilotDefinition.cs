namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// A named allied pilot who fights alongside the player under AI control. Pilots persist across the campaign:
/// <see cref="Assembly"/> is their starting Crest, which the player can refit in the Workshop.
/// </summary>
public sealed record PilotDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Short name used in combat logs and the HUD (falls back to <see cref="Name"/>).</summary>
    public string Callsign { get; init; } = "";

    public string Description { get; init; } = "";
    public required CrestConfiguration Assembly { get; init; }

    public string DisplayName => Callsign.Length > 0 ? Callsign : Name;
}
