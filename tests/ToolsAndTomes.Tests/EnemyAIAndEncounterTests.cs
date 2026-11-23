using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class EnemyAIAndEncounterTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void EnemyAI_SelectsValidArchetypeActions_UsingStandardSimulationRules()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var sim = EncounterFactory.CreateTestEncounter(loadout, seed: 101);
        var ai = new EnemyAIController(decisionIntervalBeats: 1);

        for (int i = 0; i < 3; i++)
        {
            ai.Update(sim);
            sim.StepBeat();
        }

        var skirmisher = sim.FindCombatant("enemy_1_skirmisher")!;
        var bruiser = sim.FindCombatant("enemy_2_bruiser")!;
        var artillery = sim.FindCombatant("enemy_3_artillery")!;

        Assert.AreNotEqual(ActionState.Idle, skirmisher.State);
        Assert.AreNotEqual(ActionState.Idle, bruiser.State);
        Assert.AreNotEqual(ActionState.Idle, artillery.State);

        Assert.IsTrue(
            artillery.CurrentAction?.Definition.Id == "weapon_artillery_mortar" ||
            artillery.State == ActionState.Moving);
    }

    [TestMethod]
    [DataRow("cfg_agile_skirmish")]
    [DataRow("cfg_heavy_anchor")]
    public void FullEncounter_AgainstIdlePlayer_RunsToAnOutcome(string configId)
    {
        var loadout = CrestLoadout.CreateFromConfiguration(_catalog.StarterConfigurations[configId], _catalog);
        var sim = EncounterFactory.CreateTestEncounter(loadout, seed: 77);
        var ai = new EnemyAIController();

        for (int beat = 0; beat < 600 && sim.Outcome == EncounterOutcome.InProgress; beat++)
        {
            ai.Update(sim);
            sim.StepBeat();
        }

        Assert.AreEqual(EncounterOutcome.PlayerDefeat, sim.Outcome);
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.ComponentDamaged));
    }

    [TestMethod]
    public void EnemyAI_CannotUseDisabledWeaponComponent()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog);
        var sim = EncounterFactory.CreateTestEncounter(loadout, seed: 202);
        var ai = new EnemyAIController(decisionIntervalBeats: 1);

        var artillery = sim.FindCombatant("enemy_3_artillery")!;
        var mortar = artillery.Loadout.Installed.First(e => e.Definition.Id == "eq_artillery_mortar");
        mortar.ApplyDamage(mortar.Definition.MaxDurability);

        var player = sim.FindCombatant("player_crest")!;
        var cmd = ai.ChooseCommand(artillery, player, sim);

        Assert.IsFalse(cmd is UseActionCommand u && u.ActionId == "weapon_artillery_mortar");
    }

    [TestMethod]
    public void CrestConfigurationChanges_PersistComponentDamageBetweenEncounters()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var vorpal = loadout.Installed.First(e => e.Definition.Id == "eq_vorpal");
        vorpal.ApplyDamage(vorpal.Definition.MaxDurability);
        Assert.IsTrue(vorpal.IsDisabled);

        var sim = EncounterFactory.CreateTestEncounter(loadout, seed: 303);
        var player = sim.FindCombatant("player_crest")!;
        var enemy = sim.FindCombatant("enemy_2_bruiser")!;

        var attackAttempt = sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        Assert.IsFalse(attackAttempt.IsValid);

        loadout.RepairAll();
        var afterRepair = sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        Assert.IsTrue(afterRepair.IsValid);
    }
}
