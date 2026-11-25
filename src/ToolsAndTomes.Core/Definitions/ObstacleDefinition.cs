using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Definitions;

/// <summary>The two tabletop-measurable obstacle shapes.</summary>
public enum ObstacleShape
{
    /// <summary>A pillar, rock, or mound: a center and a radius.</summary>
    Circle,

    /// <summary>A straight wall: two endpoints and a thickness.</summary>
    Wall
}

/// <summary>Tall obstacles block line of fire; Low ones don't, but give partial cover.</summary>
public enum ObstacleHeight
{
    Tall,
    Low
}

/// <summary>
/// A kind of obstacle (content: <c>obstacles.json</c>). Every obstacle blocks movement. Tall ones block line of
/// fire; Low ones give <see cref="CoverDice"/> to a defender hugging them. Hard ones deal collision damage to a
/// Crest slammed into them. Durability falls to area attacks and collisions.
/// </summary>
public sealed record ObstacleDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";

    public required ObstacleShape Shape { get; init; }

    /// <summary>Circle radius (circles).</summary>
    public double Radius { get; init; }

    /// <summary>Wall thickness (walls).</summary>
    public double Thickness { get; init; }

    public ObstacleHeight Height { get; init; }

    /// <summary>Stone and iron: slamming into it hurts. Soft obstacles (rubble, wood) just stop a Crest.</summary>
    public bool Hard { get; init; }

    public int Durability { get; init; } = 1;

    /// <summary>Extra protection dice for a defender whose attacker shoots across this (Low) obstacle nearby.</summary>
    public int CoverDice { get; init; }

    /// <summary>Wreckage: what a destroyed obstacle becomes when the destroying hit leaves rubble (it keeps the footprint).</summary>
    public bool IsRubble { get; init; }

    /// <summary>Only raised by spells (e.g. Iron Wall), not placed in encounters.</summary>
    public bool ConjuredOnly { get; init; }
}

/// <summary>An obstacle in an encounter: a circle <see cref="At"/> a point, or a wall <see cref="From"/>–<see cref="To"/>.</summary>
public sealed record ObstaclePlacement
{
    public required string Type { get; init; }
    public Vec2? At { get; init; }
    public Vec2? From { get; init; }
    public Vec2? To { get; init; }
}
