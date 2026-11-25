using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

/// <summary>The campaign session both frontends share: profile, lance, and resuming a mission after a restart.</summary>
[TestClass]
public sealed class CampaignSessionTests
{
    private string _profile = "";

    [TestInitialize]
    public void Init() => _profile = Path.Combine(Path.GetTempPath(), $"tnt-session-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_profile))
            File.Delete(_profile);
    }

    private static void WinCurrentFight(CampaignSession session)
    {
        var sim = session.StartEncounter();
        sim.Resume();
        foreach (var enemy in sim.Combatants.Where(c => c.Faction == Faction.Enemy))
            foreach (var core in enemy.Loadout.Installed.Where(e => e.Definition.IsCore))
                core.ApplyDamage(core.Definition.MaxDurability);
        sim.StepBeat();
        Assert.AreEqual(EncounterOutcome.PlayerVictory, sim.Outcome);
        session.CompleteEncounter(sim);
    }

    [TestMethod]
    public void ANewProfile_HasTheStarterCrests_AndBothPilotsInTheLance()
    {
        var session = new CampaignSession(_profile);
        Assert.IsGreaterThanOrEqualTo(2, session.CrestAssemblies.Count);
        CollectionAssert.AreEqual(new[] { "pilot_marrow", "pilot_vell" }, session.Lance);
        Assert.IsTrue(session.SetLance(["pilot_vell"]));
        Assert.IsFalse(session.SetLance(["pilot_vell", "pilot_vell"]));
        Assert.IsFalse(session.SetLance(["pilot_nobody"]));

        var reloaded = new CampaignSession(_profile);
        CollectionAssert.AreEqual(new[] { "pilot_vell" }, reloaded.Lance);
    }

    [TestMethod]
    public void QuittingMidFight_ResumesAtTheStartOfThatFight()
    {
        var session = new CampaignSession(_profile);
        session.StartMission("mis_first_patrol");
        var sim = session.StartEncounter();
        sim.Resume();
        for (int i = 0; i < 5; i++)
            sim.StepBeat(); // ...then the player quits mid-fight

        var after = new CampaignSession(_profile);
        Assert.IsNotNull(after.ActiveMission);
        Assert.AreEqual(MissionPhase.Encounter, after.ActiveMission.Phase);
        Assert.AreEqual(0, after.ActiveMission.StageIndex);
        Assert.IsTrue(after.ProfileWarnings.Any(w => w.StartsWith("Resumed First Patrol")));
        Assert.IsFalse(after.SetLance([]), "the lance is fixed while a mission is in progress");
        Assert.AreEqual(0, after.StartEncounter().CurrentBeat, "the fight starts over");
    }

    [TestMethod]
    public void QuittingDuringARest_ResumesTheRest_AndFinishingUnlocksTheNextMission()
    {
        var session = new CampaignSession(_profile);
        session.StartMission("mis_first_patrol");
        WinCurrentFight(session);
        Assert.AreEqual(MissionPhase.Rest, session.ActiveMission!.Phase);
        session.ActiveMission.Wait();
        session.Save();
        int restLeft = session.ActiveMission.RestBeatsRemaining;

        var after = new CampaignSession(_profile);
        var run = after.ActiveMission!;
        Assert.AreEqual(MissionPhase.Rest, run.Phase);
        Assert.AreEqual(restLeft, run.RestBeatsRemaining);
        Assert.AreEqual(after.SelectedCrestId, run.Loadout.Id, "the mission's Crest is the selected assembly");
        Assert.AreSame(run.Loadout, after.SelectedLoadout);

        run.ContinueToNextEncounter();
        WinCurrentFight(after);
        Assert.IsNull(after.ActiveMission);
        Assert.IsTrue(after.Workshop.HasCompleted("mis_first_patrol"));

        var later = new CampaignSession(_profile);
        Assert.IsNull(later.ActiveMission, "a finished mission is no longer saved");
        Assert.IsTrue(MissionRun.IsUnlocked(later.Catalog.Missions["mis_border_patrol"], later.Workshop));
    }
}
