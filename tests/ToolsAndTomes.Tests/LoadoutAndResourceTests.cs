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
    public void WitCapacity_Is10WithSpellbook_And5Without()
    {
        var loadout = CrestLoadout.CreateFromTemplate(TemplateCatalog.Strider, _catalog);
        var withBook = LoadoutRules.ComputeStats(loadout);
        Assert.IsTrue(withBook.HasSpellbook);
        Assert.AreEqual(10, withBook.MaxWit);
        Assert.AreEqual(5, withBook.MaxMotes);

        var book = loadout.Installed.First(e => e.Definition.IsSpellbook);
        loadout.Uninstall(book.InstanceId);

        var withoutBook = LoadoutRules.ComputeStats(loadout);
        Assert.IsFalse(withoutBook.HasSpellbook);
        Assert.AreEqual(5, withoutBook.MaxWit);
        Assert.AreEqual(3, withoutBook.MaxMotes);
    }

    [TestMethod]
    public void EquipmentWitTax_UpdatesOnInstallRemoveAndDamage()
    {
        var loadout = CrestLoadout.CreateFromTemplate(TemplateCatalog.Bulwark, _catalog);
        var initialStats = LoadoutRules.ComputeStats(loadout);

        var shield = loadout.Installed.First(e => e.Definition.Id == "eq_shield_generator");
        int shieldTax = shield.Definition.WitTax;
        Assert.IsGreaterThan(0, shieldTax);
        Assert.AreEqual(2, initialStats.BonusProtectionDice); // Heavy plate + Shield Generator

        // Damage shield until disabled
        shield.ApplyDamage(shield.Definition.MaxDurability);
        Assert.IsTrue(shield.IsDisabled);

        var damagedStats = LoadoutRules.ComputeStats(loadout);
        Assert.AreEqual(initialStats.EquipmentWitTax - shieldTax, damagedStats.EquipmentWitTax);
        Assert.AreEqual(1, damagedStats.BonusProtectionDice); // Shield bonus gone

        // Reinstalling a damaged component must preserve its damage state
        var removed = loadout.Uninstall(shield.InstanceId);
        Assert.IsNotNull(removed);
        loadout.Install(removed);
        Assert.IsTrue(removed.IsDisabled);
        Assert.AreEqual(damagedStats.EquipmentWitTax, LoadoutRules.ComputeStats(loadout).EquipmentWitTax);
    }

    [TestMethod]
    public void DisabledJumpJets_AndThermalRegulator_AndAmplifier_LoseFunctionality()
    {
        var strider = CrestLoadout.CreateFromTemplate(TemplateCatalog.Strider, _catalog);
        var baseStats = LoadoutRules.ComputeStats(strider);

        var jets = strider.Installed.First(e => e.Definition.Id == "eq_jump_jets");
        var amp = strider.Installed.First(e => e.Definition.Id == "eq_spell_amplifier");

        jets.ApplyDamage(jets.Definition.MaxDurability);
        amp.ApplyDamage(amp.Definition.MaxDurability);

        var afterDamage = LoadoutRules.ComputeStats(strider);
        Assert.IsLessThan(baseStats.MoveSpeed, afterDamage.MoveSpeed);
        Assert.IsFalse(afterDamage.HasSpellAmplifier);

        var bulwark = CrestLoadout.CreateFromTemplate(TemplateCatalog.Bulwark, _catalog);
        var bulwarkBase = LoadoutRules.ComputeStats(bulwark);
        var regulator = bulwark.Installed.First(e => e.Definition.Id == "eq_thermal_regulator");
        regulator.ApplyDamage(regulator.Definition.MaxDurability);

        var bulwarkDamaged = LoadoutRules.ComputeStats(bulwark);
        Assert.IsLessThan(bulwarkBase.HeatDissipationPerSecond, bulwarkDamaged.HeatDissipationPerSecond);
    }

    [TestMethod]
    public void StriderAndBulwark_HaveDistinctEmergentTradeoffs()
    {
        var striderStats = LoadoutRules.ComputeStats(CrestLoadout.CreateFromTemplate(TemplateCatalog.Strider, _catalog));
        var bulwarkStats = LoadoutRules.ComputeStats(CrestLoadout.CreateFromTemplate(TemplateCatalog.Bulwark, _catalog));

        Assert.IsGreaterThan(bulwarkStats.MoveSpeed, striderStats.MoveSpeed);
        Assert.IsGreaterThan(striderStats.TotalWeight, bulwarkStats.TotalWeight);
        Assert.IsGreaterThan(striderStats.DisplacementResistance, bulwarkStats.DisplacementResistance);
        Assert.IsGreaterThan(striderStats.BonusProtectionDice, bulwarkStats.BonusProtectionDice);
    }

    [TestMethod]
    public void MoteGeneration_GeneratesOneEveryFiveSeconds_UpToStorageLimit()
    {
        var resources = new ResourceState(startingMotes: 0);
        int maxMotes = 3;

        for (int tick = 0; tick < LoadoutRules.MoteGenerationIntervalTicks - 1; tick++)
        {
            Assert.IsFalse(resources.AdvanceMoteTimer(LoadoutRules.MoteGenerationIntervalTicks, maxMotes));
        }

        Assert.IsTrue(resources.AdvanceMoteTimer(LoadoutRules.MoteGenerationIntervalTicks, maxMotes));
        Assert.AreEqual(1, resources.CurrentMotes);

        // Advance 25 seconds -> should cap at 3
        for (int tick = 0; tick < LoadoutRules.MoteGenerationIntervalTicks * 5; tick++)
        {
            resources.AdvanceMoteTimer(LoadoutRules.MoteGenerationIntervalTicks, maxMotes);
        }

        Assert.AreEqual(maxMotes, resources.CurrentMotes);
    }
}
