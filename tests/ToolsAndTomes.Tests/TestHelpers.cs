using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

internal sealed class FixedRandom(params int[] rolls) : ISeededRandom
{
    private int _index;

    public int RollD6() => rolls[_index++ % rolls.Length];
    public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
}

internal static class TestHelpers
{
    public static Combatant CreatePlayer(CrestConfiguration cfg, Vec2 pos, int startingMotes = 2) =>
        new("player", cfg.Name, Faction.Player,
            CrestLoadout.CreateFromConfiguration(cfg, GameCatalog.Default, "p"), pos, startingMotes);

    public static Combatant CreateEnemy(string enemyId, Vec2 pos, string? combatantId = null)
    {
        var def = GameCatalog.Default.Enemies[enemyId];
        return new Combatant(
            combatantId ?? enemyId,
            def.Name,
            Faction.Enemy,
            CrestLoadout.CreateFromConfiguration(def.Assembly, GameCatalog.Default, combatantId ?? "e"),
            pos,
            startingMotes: 2,
            def.Archetype,
            def.PreferredRange);
    }

    /// <summary>Steps beats until the condition holds; fails the test if it never does.</summary>
    public static void StepUntil(CombatSimulation sim, Func<bool> condition, int maxBeats = 30)
    {
        for (int i = 0; i < maxBeats; i++)
        {
            if (condition())
                return;
            sim.StepBeat();
        }

        Assert.IsTrue(condition(), $"Condition not reached within {maxBeats} beats.");
    }

    public static bool IsUndamaged(Combatant c) =>
        c.Loadout.Installed.All(e => e.CurrentDurability == e.Definition.MaxDurability);
}
