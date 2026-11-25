using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Definitions;

/// <summary>A battle: arena, starting positions, enemies, and what winning it yields.</summary>
public sealed record EncounterDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";

    public double ArenaWidth { get; init; } = 24;
    public double ArenaHeight { get; init; } = 14;

    public Vec2 PlayerStart { get; init; } = new(4, 7);
    public int PlayerStartingMotes { get; init; }

    /// <summary>Where allied Crests start, in lance order. Missing entries flank <see cref="PlayerStart"/>.</summary>
    public IReadOnlyList<Vec2> AllyStarts { get; init; } = [];

    /// <summary>The start position for the ally at <paramref name="index"/> (0-based).</summary>
    public Vec2 AllyStart(int index)
    {
        if (index < AllyStarts.Count)
            return AllyStarts[index];

        // Flank the player: alternate above and below, a step behind, widening for each further ally.
        double side = index % 2 == 0 ? -1 : 1;
        double offset = 2.5 * (index / 2 + 1);
        return new Vec2(PlayerStart.X - 1.5, PlayerStart.Y + side * offset)
            .Clamp(1.0, ArenaWidth - 1.0, 1.0, ArenaHeight - 1.0);
    }

    public IReadOnlyList<EnemyPlacement> Enemies { get; init; } = [];

    /// <summary>The field's layout: pillars, rocks, walls, rubble, barricades.</summary>
    public IReadOnlyList<ObstaclePlacement> Obstacles { get; init; } = [];
    public RewardTable Rewards { get; init; } = new();
}

public sealed record EnemyPlacement
{
    public required string EnemyId { get; init; }
    public required Vec2 Position { get; init; }
    public int StartingMotes { get; init; }
}

/// <summary>
/// Material samples granted on victory: every guaranteed entry, plus <see cref="Draws"/> weighted random
/// picks from <see cref="Pool"/>. Rolls use the encounter seed, so a given seed always yields the same rewards.
/// </summary>
public sealed record RewardTable
{
    public IReadOnlyList<MaterialCount> Guaranteed { get; init; } = [];
    public int Draws { get; init; }
    public IReadOnlyList<WeightedMaterial> Pool { get; init; } = [];
}

public sealed record MaterialCount(string MaterialId, int Count);

public sealed record WeightedMaterial(string MaterialId, int Weight);
