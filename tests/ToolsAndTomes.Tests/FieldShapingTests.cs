using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class FieldShapingTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    private static UseActionCommand Shape(Combatant c, ShapingKind kind, string? itemId = null, string? ownComponent = null) =>
        new(c.Id, "spell_shape_fast_iron", OwnComponentInstanceId: ownComponent, Shaping: new ShapingOrder(kind, itemId));

    [TestMethod]
    public void AgileStarter_CarriesFastIronAndACharge_WhichAddWeight()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var stats = loadout.GetStats();

        Assert.AreEqual(3, loadout.CarriedFastIron);
        Assert.AreEqual(1, loadout.GetConsumableCount("con_shaped_charge"));
        Assert.AreEqual(4, stats.CargoCapacity);
        Assert.AreEqual(4, stats.CargoUsed);
        Assert.AreEqual(loadout.Installed.Sum(e => e.Definition.Weight) + 3 * 2 + 2, stats.TotalWeight);
    }

    [TestMethod]
    public void DestroyedWeapon_CanBeReshapedMidFight_AndJoinsTheEndOfTheDamageOrder()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));
        var vorpal = player.Loadout.Installed.First(e => e.Definition.Id == "eq_vorpal");
        vorpal.ApplyDamage(vorpal.Definition.MaxDurability);
        Assert.IsFalse(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id)).IsValid);

        int cost = ShapingRules.ComponentCost(_catalog.GetEquipment("eq_vorpal"), RulesConfig.Default);
        Assert.AreEqual(1, cost, "Vorpal weighs 6 → ceil(6/10) = 1 fast-iron.");

        Assert.IsTrue(sim.IssueCommand(Shape(player, ShapingKind.Component, "eq_vorpal")).IsValid);
        StepUntil(sim, () => sim.Events.Any(e => e.Type == CombatEventType.FieldShaped));

        var shaped = player.Loadout.DamageOrder[^1];
        Assert.AreEqual("eq_vorpal", shaped.Definition.Id);
        Assert.IsTrue(shaped.IsOperational);
        Assert.IsTrue(player.Loadout.DamageOrder[^2].Definition.IsCore, "Shaped parts go after the Core.");
        Assert.AreEqual(2, player.Loadout.CarriedFastIron);
        StepUntil(sim, () => player.State == ActionState.Idle);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id)).IsValid);
    }

    [TestMethod]
    public void Shaping_SpendsLooseFastIronBeforeCargo()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        player.Resources.AddFastIron(1);
        Assert.AreEqual(4, player.TotalFastIron);

        Assert.IsTrue(player.TrySpendFastIron(2));
        Assert.AreEqual(0, player.Resources.FastIron);
        Assert.AreEqual(2, player.Loadout.CarriedFastIron);
        Assert.IsFalse(player.TrySpendFastIron(3));
    }

    [TestMethod]
    public void ShapingAmmo_RefillsAWeapon_AndAmmoPacksDoTheSame()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(4, 5));
        var rack = new EquipmentInstance("rack", _catalog.GetEquipment("util_cargo_rack"));
        player.Loadout.Install(rack);
        player.Loadout.SetCargoManifest([new CargoEntry(CargoKind.FastIron, 1), new CargoEntry(CargoKind.Consumable, 1, "con_ammo_pack")], _catalog);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 4);

        var breath = player.Loadout.Installed.First(e => e.Definition.Id == "eq_dragons_breath");
        Assert.IsFalse(sim.IssueCommand(Shape(player, ShapingKind.AmmoRefill, ownComponent: breath.InstanceId)).IsValid, "Already full.");

        breath.TryConsumeAmmo(breath.CurrentAmmo);
        Assert.IsTrue(sim.IssueCommand(Shape(player, ShapingKind.AmmoRefill, ownComponent: breath.InstanceId)).IsValid);
        StepUntil(sim, () => breath.CurrentAmmo == breath.Definition.MaxAmmo);
        Assert.AreEqual(0, player.TotalFastIron);

        breath.TryConsumeAmmo(2);
        StepUntil(sim, () => player.State == ActionState.Idle);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "use_ammo_pack", OwnComponentInstanceId: breath.InstanceId)).IsValid);
        StepUntil(sim, () => breath.CurrentAmmo == breath.Definition.MaxAmmo);
        Assert.AreEqual(0, player.Loadout.GetConsumableCount("con_ammo_pack"));
    }

    [TestMethod]
    public void ShapedConsumables_GoIntoCargo_OnlyWhenThereIsRoom()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 4);

        // Cargo is full (3 fast-iron + 1 charge = 4/4): shaping a ward needs room.
        Assert.IsFalse(sim.IssueCommand(Shape(player, ShapingKind.Consumable, "con_iron_ward")).IsValid);

        // Throw the charge to make room, then shape a ward from 2 of the carried fast-iron.
        player.Loadout.TryTakeConsumable("con_shaped_charge");
        Assert.IsTrue(sim.IssueCommand(Shape(player, ShapingKind.Consumable, "con_iron_ward")).IsValid);
        StepUntil(sim, () => player.Loadout.GetConsumableCount("con_iron_ward") == 1);

        Assert.AreEqual(1, player.Loadout.CarriedFastIron);
        Assert.AreEqual(2, player.Stats.CargoUsed);
    }

    [TestMethod]
    public void InterruptedShaping_SpendsNothing()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 4);

        sim.IssueCommand(Shape(player, ShapingKind.Component, "eq_talon"));
        sim.StepBeat();
        var shaping = player.ActionsInMotion.Single();
        Assert.AreEqual(ActionState.Windup, shaping.Phase);

        var hit = new IncomingHit(enemy.Id, "Jolt", player.Id, enemy.Position, DamagePoints: 0, PushDistance: 0, InterruptionPower: 3);
        new DamageResolutionService().ResolveActorBatch(player, [hit], sim.CurrentBeat, new FixedRandom(6), 24, 14, []);

        Assert.IsEmpty(player.ActionsInMotion);
        Assert.AreEqual(3, player.TotalFastIron);
    }

    [TestMethod]
    public void UnresearchedBlueprints_CannotBeShaped()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        player.KnownBlueprints = _catalog.BaselineComponentIds;
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 4);

        var locked = sim.IssueCommand(Shape(player, ShapingKind.Component, "eq_righteous"));
        Assert.IsFalse(locked.IsValid);
        Assert.Contains("not researched", locked.FailureReason!);
        Assert.IsTrue(sim.IssueCommand(Shape(player, ShapingKind.Component, "eq_talon")).IsValid);
    }

    [TestMethod]
    public void DisabledCargoRack_LocksItsContents_AndRepairRestocks()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(8, 5));
        var sim = new CombatSimulation([player, enemy], seed: 4);
        var rack = player.Loadout.Installed.First(e => e.Definition.Id == "util_cargo_rack");

        rack.ApplyDamage(rack.Definition.MaxDurability);
        Assert.IsFalse(sim.IssueCommand(new UseActionCommand(player.Id, "throw_shaped_charge", enemy.Id)).IsValid);

        player.Loadout.TryTakeCarriedFastIron(3);
        player.Loadout.RepairAll();
        Assert.AreEqual(3, player.Loadout.CarriedFastIron, "The Workshop restocks cargo to the manifest.");
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "throw_shaped_charge", enemy.Id)).IsValid);
    }
}
