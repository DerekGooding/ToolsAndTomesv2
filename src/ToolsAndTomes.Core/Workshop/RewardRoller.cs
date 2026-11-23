using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Workshop;

public static class RewardRoller
{
    /// <summary>Separates the reward roll from the combat dice, so rewards never change combat results.</summary>
    private const int RewardStreamSalt = 0x5EED_0A7;

    /// <summary>
    /// Material samples for winning <paramref name="encounter"/> with <paramref name="seed"/>: all guaranteed
    /// entries, then weighted draws from the pool. Deterministic per seed; merged by material.
    /// At the table: roll on the encounter's reward table once per draw.
    /// </summary>
    public static IReadOnlyList<MaterialCount> Roll(EncounterDefinition encounter, int seed)
    {
        var totals = new Dictionary<string, int>();
        void Add(string id, int n) => totals[id] = (totals.TryGetValue(id, out int have) ? have : 0) + n;

        foreach (var g in encounter.Rewards.Guaranteed)
            Add(g.MaterialId, g.Count);

        var pool = encounter.Rewards.Pool;
        int totalWeight = pool.Sum(p => p.Weight);
        if (totalWeight > 0)
        {
            var rng = new SeededRandom(seed ^ RewardStreamSalt);
            for (int i = 0; i < encounter.Rewards.Draws; i++)
            {
                int pick = rng.NextInt(0, totalWeight);
                foreach (var entry in pool)
                {
                    if (pick < entry.Weight)
                    {
                        Add(entry.MaterialId, 1);
                        break;
                    }
                    pick -= entry.Weight;
                }
            }
        }

        return totals
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new MaterialCount(kv.Key, kv.Value))
            .ToList();
    }
}
