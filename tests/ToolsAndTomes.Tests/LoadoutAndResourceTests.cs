using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class LoadoutAndResourceTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void WitCapacity_Is10WithSpellbook_And5Without_PlusCoreBonuses()
    {
        var assembly = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var withBook = LoadoutRules.ComputeStats(assembly);
        Assert.IsTrue(withBook.HasSpellbook);
        Assert.AreEqual(10, withBook.MaxWit);
        Assert.AreEqual(5, withBook.MaxMotes);

        var book = assembly.Installed.First(e => e.Definition.IsSpellbook);
        assembly.Uninstall(book.InstanceId);

        var withoutBook = LoadoutRules.ComputeStats(assembly);
        Assert.IsFalse(withoutBook.HasSpellbook);
        Assert.AreEqual(5, withoutBook.MaxWit);
        Assert.AreEqual(3, withoutBook.MaxMotes);

        // Installing a Heavy Aether Furnace expands Wit capacity (+2), Mote storage (+1), and accelerates Mote generation
        var furnace = new EquipmentInstance("furnace_1", _catalog.GetEquipment("core_aether_furnace"));
        assembly.Install(furnace);
        var withFurnace = LoadoutRules.ComputeStats(assembly);
        Assert.AreEqual(7, withFurnace.MaxWit);
        Assert.AreEqual(4, withFurnace.MaxMotes);
        Assert.IsLessThan(LoadoutRules.MoteGenerationIntervalBeats, withFurnace.MoteGenerationIntervalBeats);
    }

    [TestMethod]
    public void EquipmentWitTax_UpdatesOnInstallRemoveAndDamage()
    {
        var assembly = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog);
        var initialStats = LoadoutRules.ComputeStats(assembly);

        var shield = assembly.Installed.First(e => e.Definition.Id == "eq_shield_generator");
        int shieldTax = shield.Definition.WitTax;
        Assert.IsGreaterThan(0, shieldTax);
        Assert.AreEqual(2, initialStats.BonusProtectionDice); // Heavy plate + Shield Generator

        shield.ApplyDamage(shield.Definition.MaxDurability);
        Assert.IsTrue(shield.IsDisabled);

        var damagedStats = LoadoutRules.ComputeStats(assembly);
        Assert.AreEqual(initialStats.EquipmentWitTax - shieldTax, damagedStats.EquipmentWitTax);
        Assert.AreEqual(1, damagedStats.BonusProtectionDice);

        // Reinstalling a damaged component must preserve its damage state
        var removed = assembly.Uninstall(shield.InstanceId);
        Assert.IsNotNull(removed);
        assembly.Install(removed);
        Assert.IsTrue(removed.IsDisabled);
        Assert.AreEqual(damagedStats.EquipmentWitTax, LoadoutRules.ComputeStats(assembly).EquipmentWitTax);
    }

    [TestMethod]
    public void DisabledMobilityThermalAndAmplifierComponents_DegradeDerivedCapabilities()
    {
        var agile = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var baseStats = LoadoutRules.ComputeStats(agile);

        var jets = agile.Installed.First(e => e.Definition.Id == "eq_jump_jets");
        var amp = agile.Installed.First(e => e.Definition.Id == "eq_spell_amplifier");

        jets.ApplyDamage(jets.Definition.MaxDurability);
        amp.ApplyDamage(amp.Definition.MaxDurability);

        var afterDamage = LoadoutRules.ComputeStats(agile);
        Assert.IsLessThan(baseStats.MoveSpeed, afterDamage.MoveSpeed);
        Assert.IsFalse(afterDamage.HasSpellAmplifier);

        var heavy = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog);
        var heavyBase = LoadoutRules.ComputeStats(heavy);
        var regulator = heavy.Installed.First(e => e.Definition.Id == "eq_thermal_regulator");
        regulator.ApplyDamage(regulator.Definition.MaxDurability);

        var heavyDamaged = LoadoutRules.ComputeStats(heavy);
        Assert.IsLessThan(heavyBase.HeatDissipationPerBeat, heavyDamaged.HeatDissipationPerBeat);
    }

    [TestMethod]
    public void DistinctComponentAssemblies_ProduceEmergentTradeoffsWithoutChassis()
    {
        var agileStats = LoadoutRules.ComputeStats(CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog));
        var heavyStats = LoadoutRules.ComputeStats(CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog));

        Assert.IsGreaterThan(heavyStats.MoveSpeed, agileStats.MoveSpeed);
        Assert.IsGreaterThan(agileStats.TotalWeight, heavyStats.TotalWeight);
        Assert.IsGreaterThan(agileStats.DisplacementResistance, heavyStats.DisplacementResistance);
        Assert.IsGreaterThan(agileStats.BonusProtectionDice, heavyStats.BonusProtectionDice);
        Assert.IsGreaterThan(agileStats.MaxWit, heavyStats.MaxWit); // Heavy Aether Furnace contributes +2 Wit
    }

    [TestMethod]
    public void SavedConfigurationsAndComponentTransfer_PreserveAssemblyAndDamageState()
    {
        var assemblyA = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "a");
        var assemblyB = new CrestLoadout("custom_b", "Experimental Rig");

        var blink = assemblyA.Installed.First(e => e.Definition.Id == "eq_blink_drive");
        blink.ApplyDamage(1);

        // Transferring component instance moves it directly with its damage intact
        Assert.IsTrue(assemblyA.TransferComponentTo(blink.InstanceId, assemblyB));
        Assert.IsNull(assemblyA.FindById(blink.InstanceId));
        var transferred = assemblyB.FindById(blink.InstanceId);
        Assert.IsNotNull(transferred);
        Assert.AreEqual(blink.Definition.MaxDurability - 1, transferred.CurrentDurability);

        // Exporting and restoring a configuration reproduces the same derived stats deterministically
        var saved = assemblyA.ExportConfiguration("saved_a");
        var restored = CrestLoadout.CreateFromConfiguration(saved, _catalog, "r");
        Assert.AreEqual(LoadoutRules.ComputeStats(assemblyA), LoadoutRules.ComputeStats(restored));
    }

    [TestMethod]
    public void MoteGeneration_GeneratesOneEveryFiveSeconds_UpToStorageLimit()
    {
        var resources = new ResourceState(startingMotes: 0);
        int maxMotes = 3;

        for (int beat = 0; beat < LoadoutRules.MoteGenerationIntervalBeats - 1; beat++)
            Assert.IsFalse(resources.AdvanceMoteTimer(LoadoutRules.MoteGenerationIntervalBeats, maxMotes));

        Assert.IsTrue(resources.AdvanceMoteTimer(LoadoutRules.MoteGenerationIntervalBeats, maxMotes));
        Assert.AreEqual(1, resources.CurrentMotes);

        for (int beat = 0; beat < LoadoutRules.MoteGenerationIntervalBeats * 5; beat++)
            resources.AdvanceMoteTimer(LoadoutRules.MoteGenerationIntervalBeats, maxMotes);

        Assert.AreEqual(maxMotes, resources.CurrentMotes);
    }
}
