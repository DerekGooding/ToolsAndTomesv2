namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// An exotic material discovered as encounter rewards. Researching it at the Research Desk consumes
/// <see cref="SamplesToResearch"/> samples and permanently unlocks blueprints that the Workshop can then
/// fabricate from fast-iron (free in the Workshop, where time is not a factor).
/// </summary>
public sealed record ExoticMaterialDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public int SamplesToResearch { get; init; } = 1;

    /// <summary>Component blueprints unlocked once this material is researched.</summary>
    public IReadOnlyList<string> UnlocksComponentIds { get; init; } = [];
}
