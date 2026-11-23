using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class DamageOrderTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void PenetratingDamage_FillsComponentsInDamageOrder_AndOverflowsToTheNext()
    {
        var target = new Combatant("target", "Target", Faction.Enemy, new CrestLoadout("t", "T",
        [
            new EquipmentInstance("a", _catalog.GetEquipment("eq_talon")),          // durability 2
            new EquipmentInstance("b", _catalog.GetEquipment("eq_jump_jets")),      // durability 2
            new EquipmentInstance("core", _catalog.GetEquipment("core_aether_dynamo"))
        ]), new Vec2(5, 5));

        var service = new DamageResolutionService();
        var events = new List<CombatEvent>();
        var hit = new IncomingHit("src", "Test Blow", target.Id, new Vec2(4, 5), DamagePoints: 3, PushDistance: 0, InterruptionPower: 0);

        service.ResolveActorBatch(target, [hit], 0, new FixedRandom(1), 24, 14, events);

        Assert.IsTrue(target.Loadout.FindById("a")!.IsDisabled, "First in order absorbs first.");
        Assert.AreEqual(1, target.Loadout.FindById("b")!.CurrentDurability, "Remaining point overflows to the next.");
        Assert.AreEqual(3, target.Loadout.FindById("core")!.CurrentDurability, "Core untouched while earlier parts remain.");
    }

    [TestMethod]
    public void CoreEarlyInDamageOrder_TakesDamageFirst_AndIsWarnedAgainst()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "p");
        Assert.IsEmpty(LoadoutRules.GetWarnings(loadout));

        var core = loadout.Installed.First(e => e.Definition.IsCore);
        loadout.MoveInDamageOrder(core.InstanceId, -100);
        Assert.AreSame(core, loadout.DamageOrder[0]);

        var warnings = LoadoutRules.GetWarnings(loadout);
        Assert.IsTrue(warnings.Any(w => w.Contains("Core is not last")));

        var target = new Combatant("t", "T", Faction.Enemy, loadout, new Vec2(5, 5));
        Assert.AreSame(core, DamageResolutionService.SelectHitComponent(target));
    }

    [TestMethod]
    public void Install_PlacesNewComponentsAheadOfTrailingCore()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "p");
        var plate = new EquipmentInstance("plate", _catalog.GetEquipment("armor_heavy_plate"));

        loadout.Install(plate);

        Assert.IsTrue(loadout.DamageOrder[^1].Definition.IsCore);
        Assert.AreSame(plate, loadout.DamageOrder[^2]);
    }

    [TestMethod]
    public void DamageOrder_SurvivesExportAndRestore()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog, "p");
        var titan = loadout.Installed.First(e => e.Definition.Id == "eq_titan");
        loadout.MoveInDamageOrder(titan.InstanceId, -10);

        var restored = CrestLoadout.CreateFromConfiguration(loadout.ExportConfiguration("copy"), _catalog, "r");

        CollectionAssert.AreEqual(
            loadout.DamageOrder.Select(e => e.Definition.Id).ToList(),
            restored.DamageOrder.Select(e => e.Definition.Id).ToList());
        Assert.AreEqual("eq_titan", restored.DamageOrder[0].Definition.Id);
    }

    [TestMethod]
    public void StarterAndEnemyBuilds_KeepCoreLast()
    {
        var configs = _catalog.StarterConfigurations.Values.Concat(_catalog.Enemies.Values.Select(e => e.Assembly));
        foreach (var cfg in configs)
        {
            var loadout = CrestLoadout.CreateFromConfiguration(cfg, _catalog);
            Assert.IsFalse(LoadoutRules.GetWarnings(loadout).Any(w => w.Contains("Core")), cfg.Id);
        }
    }

    [TestMethod]
    public void WitOverBudget_IsWarned()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "p");
        foreach (var id in new[] { "eq_falconet", "eq_shield_generator", "eq_artillery_mortar" })
            loadout.Install(new EquipmentInstance("x_" + id, _catalog.GetEquipment(id)));

        Assert.IsTrue(LoadoutRules.GetWarnings(loadout).Any(w => w.Contains("Wit over budget")));
    }
}
