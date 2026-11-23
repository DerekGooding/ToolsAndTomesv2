using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class RulesConfigTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void CachedStats_RefreshWhenComponentsBreakRepairOrChange()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog);
        var first = loadout.GetStats();
        Assert.AreSame(first, loadout.GetStats(), "Unchanged loadout reuses the cached stats.");

        var shield = loadout.Installed.First(e => e.Definition.Id == "eq_shield_generator");
        shield.ApplyDamage(1);
        Assert.AreSame(first, loadout.GetStats(), "Durability loss without disabling does not change stats.");

        shield.ApplyDamage(1);
        var disabled = loadout.GetStats();
        Assert.AreEqual(first.BonusProtectionDice - 1, disabled.BonusProtectionDice);

        shield.Repair();
        Assert.AreEqual(first, loadout.GetStats());

        loadout.Install(new EquipmentInstance("extra", _catalog.GetEquipment("eq_jump_jets")));
        Assert.AreEqual(first.TotalWeight + 6, loadout.GetStats().TotalWeight);

        Assert.AreEqual(LoadoutRules.ComputeStats(loadout), loadout.GetStats(), "Cache always matches a fresh computation.");
    }

    [TestMethod]
    public void CustomRules_ChangeDerivedStatsAndCombat()
    {
        var fastMotes = RulesConfig.Default with { MoteGenerationIntervalBeats = 2, BaseMoteStorage = 6 };
        var catalog = new GameCatalog(_catalog.Actions, _catalog.Equipment, _catalog.StarterConfigurations, _catalog.Enemies, fastMotes);

        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, catalog);
        var stats = loadout.GetStats(fastMotes);
        Assert.AreEqual(2, stats.MoteGenerationIntervalBeats);
        Assert.AreEqual(8, stats.MaxMotes);

        var player = new Combatant("player", "P", Faction.Player, loadout, new Vec2(4, 5), startingMotes: 0);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 1, catalog: catalog);

        for (int i = 0; i < 4; i++)
            sim.StepBeat();

        Assert.AreEqual(2, player.Resources.CurrentMotes, "One Mote every 2 beats under the custom rules.");
    }

    [TestMethod]
    public void CustomSaveTarget_IsUsedForProtectionRolls()
    {
        var easySaves = new DamageResolutionService(RulesConfig.Default with { ProtectionSaveTarget = 3 });
        Assert.IsTrue(easySaves.RollProtection(0, new FixedRandom(3)).Saved);
        Assert.IsFalse(new DamageResolutionService().RollProtection(0, new FixedRandom(3)).Saved);
    }
}
