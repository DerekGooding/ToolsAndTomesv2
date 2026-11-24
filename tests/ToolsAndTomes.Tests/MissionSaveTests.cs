using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;

namespace ToolsAndTomes.Tests;

/// <summary>Saving a mission between fights and resuming it exactly.</summary>
[TestClass]
public sealed class MissionSaveTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    private MissionRun StartPatrol() =>
        MissionRun.Start(_catalog.Missions["mis_border_patrol"],
            CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "asm1"), _catalog, seed: 21,
            allies: EncounterFactory.CreateAllies(["pilot_marrow", "pilot_vell"], _catalog, 2));

    /// <summary>Plays the current encounter to the end with every side on autopilot.</summary>
    private static CombatSimulation Fight(MissionRun run)
    {
        var sim = run.BeginEncounter();
        var enemies = new EnemyAIController();
        var allies = new AllyAIController();
        var pilot = new AutoPilot();
        while (sim.Outcome == EncounterOutcome.InProgress && sim.CurrentBeat < 300)
        {
            enemies.Update(sim);
            allies.Update(sim);
            pilot.Update(sim, run.Crest);
            sim.StepBeat();
        }
        return sim;
    }

    /// <summary>Round-trips a save through the profile serializer, as the app stores it.</summary>
    private MissionSave ThroughProfile(MissionSave save)
    {
        var profile = new PlayerProfile { Assemblies = [ConfigurationCatalog.AgileSkirmish], ActiveMission = save };
        return ProfileSerializer.Deserialize(ProfileSerializer.Serialize(profile), _catalog).Profile.ActiveMission!;
    }

    private static string Json(MissionSave save) => JsonSerializer.Serialize(save, ContentSerializer.Options);

    /// <summary>A mission after one won fight and some rest activity: damage, ammo, Motes, fast-iron, a gift.</summary>
    private MissionRun MidMission()
    {
        for (int seed = 21; seed < 60; seed++)
        {
            var run = MissionRun.Start(_catalog.Missions["mis_border_patrol"],
                CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "asm1"), _catalog, seed,
                allies: EncounterFactory.CreateAllies(["pilot_marrow", "pilot_vell"], _catalog, 2));
            if (Fight(run).Outcome != EncounterOutcome.PlayerVictory)
                continue;
            run.FinishEncounter();
            run.Wait();
            run.SquareSummon();
            if (run.Crest.TotalFastIron > 0)
                run.GiveFastIron(null, run.Allies[1].Id);
            return run;
        }
        Assert.Fail("no seed won the first fight");
        return null!;
    }

    [TestMethod]
    public void ASaveDuringARest_ResumesIdentically()
    {
        var run = MidMission();
        Assert.AreEqual(MissionPhase.Rest, run.Phase);
        var save = run.CreateSave();

        var resumed = MissionRun.TryResume(ThroughProfile(save), _catalog, null, out var error);
        Assert.IsNotNull(resumed, error);
        Assert.AreEqual(Json(save), Json(resumed.CreateSave()), "everything saved comes back");
        Assert.AreEqual(run.RestBeatsRemaining, resumed.RestBeatsRemaining);
        Assert.AreEqual("pilot_vell", resumed.PilotOf(resumed.Allies[1]));

        // The rest of the mission plays out the same from either copy.
        run.ContinueToNextEncounter();
        resumed.ContinueToNextEncounter();
        var a = Fight(run);
        var b = Fight(resumed);
        Assert.AreEqual(a.Outcome, b.Outcome);
        Assert.AreEqual(a.CurrentBeat, b.CurrentBeat);
        CollectionAssert.AreEqual(a.Events.Select(e => e.Message).ToList(), b.Events.Select(e => e.Message).ToList());
    }

    [TestMethod]
    public void ASaveBeforeAFight_ResumesAtTheStartOfThatFight()
    {
        var run = MidMission();
        run.ContinueToNextEncounter();
        var save = run.CreateSave();
        Assert.AreEqual(MissionPhase.Encounter, save.Phase);
        Assert.AreEqual(1, save.StageIndex);

        // Mid-fight the mission can't be saved; the last snapshot is the one that counts.
        run.BeginEncounter();
        Assert.IsFalse(run.CanSave);
        Assert.ThrowsExactly<InvalidOperationException>(() => run.CreateSave());

        var resumed = MissionRun.TryResume(ThroughProfile(save), _catalog, null, out _)!;
        Assert.AreEqual(MissionPhase.Encounter, resumed.Phase);
        Assert.AreEqual(_catalog.Encounters["enc_bm_iron_column"].Id, resumed.CurrentEncounter.Id);
        var sim = resumed.BeginEncounter();
        Assert.AreEqual(resumed.Lance.Count(c => !c.IsDefeated), sim.Combatants.Count(c => c.Faction == Faction.Player),
            "every standing lance member comes along");
    }

    [TestMethod]
    public void ShapedPartsAndDowned_Allies_SurviveTheSave()
    {
        var run = MidMission();
        var marrow = run.Allies[0];
        foreach (var core in marrow.Loadout.Installed.Where(e => e.Definition.IsCore))
            core.ApplyDamage(core.Definition.MaxDurability);
        var spare = new EquipmentInstance("asm1_rest_shaped_eq_talon", _catalog.GetEquipment("eq_talon"));
        spare.ApplyDamage(1);
        run.Crest.Loadout.InstallAtEnd(spare);

        var resumed = MissionRun.TryResume(ThroughProfile(run.CreateSave()), _catalog, null, out _)!;
        Assert.IsTrue(resumed.Allies[0].IsDefeated, "a downed ally stays down");
        var shaped = resumed.Crest.Loadout.FindById("asm1_rest_shaped_eq_talon");
        Assert.IsNotNull(shaped);
        Assert.AreEqual(1, shaped.CurrentDurability);
        Assert.AreSame(shaped, resumed.Crest.Loadout.DamageOrder[^1], "shaped parts keep their place in the Damage Order");
        Assert.AreEqual(run.Crest.Loadout.Id, resumed.Loadout.Id, "the app swaps it in for the same assembly");
    }

    [TestMethod]
    public void SavesThatNoLongerMatchTheContent_AreRejectedWithAReason()
    {
        var save = MidMission().CreateSave();

        Assert.IsNull(MissionRun.TryResume(save with { MissionId = "mis_gone" }, _catalog, null, out var e1));
        StringAssert.Contains(e1, "mis_gone");

        var broken = save.Lance[0] with { Assembly = save.Lance[0].Assembly with { ComponentIds = [.. save.Lance[0].Assembly.ComponentIds.SkipLast(1), "eq_gone"] } };
        Assert.IsNull(MissionRun.TryResume(save with { Lance = [broken, .. save.Lance.Skip(1)] }, _catalog, null, out var e2));
        StringAssert.Contains(e2, "eq_gone");

        Assert.IsNull(MissionRun.TryResume(save with { StageIndex = 2 }, _catalog, null, out var e3), "no rest after the last fight");
        Assert.IsNotNull(e3);
    }
}
