namespace ToolsAndTomes.Core.Definitions;

public sealed record CrestTemplateDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public double BaseMoveSpeed { get; init; }
    public double BaseWeight { get; init; }
    public double BaseDisplacementResistance { get; init; }
    public double BaseHeatCapacity { get; init; } = 10.0;
    public double BaseHeatDissipationPerSecond { get; init; } = 1.0;
    public IReadOnlyList<string> DefaultEquipmentIds { get; init; } = [];
}

public sealed record EnemyDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EnemyArchetype Archetype { get; init; }
    public required CrestTemplateDefinition Template { get; init; }
    public double PreferredRange { get; init; }
}
