namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// A multi-part campaign mission: encounters fought in order, with a timed rest in the field between each.
/// Damage, ammo, cargo, Motes, and fast-iron carry through the whole mission; the Workshop only repairs and
/// restocks before it starts. Missions unlock when all <see cref="RequiredResearch"/> materials are researched.
/// </summary>
public sealed record MissionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";

    /// <summary>Materials that must be researched before this mission can be chosen.</summary>
    public IReadOnlyList<string> RequiredResearch { get; init; } = [];

    public IReadOnlyList<string> EncounterIds { get; init; } = [];

    /// <summary>Time budget (beats) for each rest in the field between encounters.</summary>
    public int RestBeats { get; init; }

    /// <summary>Samples granted for completing every encounter (on top of each encounter's own rewards).</summary>
    public IReadOnlyList<MaterialCount> CompletionRewards { get; init; } = [];
}
