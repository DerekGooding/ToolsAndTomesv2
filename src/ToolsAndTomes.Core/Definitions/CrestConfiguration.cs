namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// A named, reusable list of component IDs representing a saved Crest assembly.
/// Contains no chassis or hardcoded Crest statistics; all capabilities emerge from its components.
/// </summary>
public sealed record CrestConfiguration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> ComponentIds { get; init; } = [];

    /// <summary>Pre-set reactions, in priority order.</summary>
    public IReadOnlyList<ReactionRule> ReactionRules { get; init; } = [];

    /// <summary>Cargo loaded in the Workshop; the Crest is restocked to this manifest before each encounter.</summary>
    public IReadOnlyList<CargoEntry> Cargo { get; init; } = [];

    /// <summary>
    /// Handhelds held by an extra limb or stowed in a harness, as indices into <see cref="ComponentIds"/>.
    /// Every other handheld goes in the Crest's own hands, in Damage Order; extras beyond its hands are unheld.
    /// </summary>
    public IReadOnlyList<Holding> Holdings { get; init; } = [];
}

/// <summary>The component at <see cref="Held"/> is held (limb) or stowed (harness) by the component at <see cref="Holder"/>.</summary>
public sealed record Holding(int Held, int Holder);

public sealed record EnemyDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EnemyArchetype Archetype { get; init; }
    public required CrestConfiguration Assembly { get; init; }
    public double PreferredRange { get; init; }
}
