using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class TraitAndWeightTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void Acid_DealsExtraDamageToCrestsWithOperationalArmor()
    {
        var acid = _catalog.GetAction("weapon_acid_spray");
        var armored = CreateEnemy("enemy_bruiser", new Vec2(5, 5));     // heavy plate
        var unarmored = CreateEnemy("enemy_skirmisher", new Vec2(5, 5));

        Assert.AreEqual(2, TraitRules.DamagePerHit(acid, armored));
        Assert.AreEqual(1, TraitRules.DamagePerHit(acid, unarmored));
    }

    [TestMethod]
    public void Acid_ChecksCurrentArmorState_NotDamageOrder()
    {
        var acid = _catalog.GetAction("weapon_acid_spray");
        var target = CreateEnemy("enemy_bruiser", new Vec2(5, 5));
        var plate = target.Loadout.Installed.First(e => e.Definition.IsArmor);

        // Armor moved to the back of the Damage Order still counts while operational.
        target.Loadout.MoveInDamageOrder(plate.InstanceId, +10);
        Assert.AreEqual(2, TraitRules.DamagePerHit(acid, target));

        plate.ApplyDamage(plate.Definition.MaxDurability);
        Assert.AreEqual(1, TraitRules.DamagePerHit(acid, target), "Destroyed armor no longer counts.");
    }

    [TestMethod]
    public void Gravity_DealsExtraDamageToHeavyCrests()
    {
        var gravity = _catalog.GetAction("spell_gravity_crush");
        Assert.AreEqual(3, TraitRules.DamagePerHit(gravity, CreateEnemy("enemy_bruiser", new Vec2(5, 5))));
        Assert.AreEqual(1, TraitRules.DamagePerHit(gravity, CreateEnemy("enemy_skirmisher", new Vec2(5, 5))));
    }

    [TestMethod]
    public void Weight_SlowsOnlyBeyondLegLoadCapacity_AndNeverImmobilizes()
    {
        var heavy = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog);
        var heavyStats = LoadoutRules.ComputeStats(heavy);
        Assert.AreEqual(150, heavyStats.LoadCapacity);
        Assert.AreEqual(3, heavyStats.MoveSpeed, "181 weight on 150-rated legs: 31 over, under one 40-unit penalty step.");

        // Swap to light legs: badly overloaded, slowed but still mobile.
        var anchor = heavy.Installed.First(e => e.Definition.Id == "mob_anchor_legs");
        heavy.Uninstall(anchor.InstanceId);
        heavy.Install(new EquipmentInstance("stride", _catalog.GetEquipment("mob_stride_legs")));
        var overloaded = LoadoutRules.ComputeStats(heavy);
        Assert.AreEqual(70, overloaded.LoadCapacity);
        Assert.AreEqual(3, overloaded.MoveSpeed, "Stride 5 - (165-70)/40 = 5 - 2.");

        // No legs at all: cannot walk.
        var stride = heavy.Installed.First(e => e.Definition.Id == "mob_stride_legs");
        stride.ApplyDamage(stride.Definition.MaxDurability);
        Assert.AreEqual(0, LoadoutRules.ComputeStats(heavy).MoveSpeed);
    }
}
