using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class MissionTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    private MissionRun StartPatrol(CrestConfiguration? build = null) =>
        MissionRun.Start(_catalog.Missions["mis_border_patrol"],
            CrestLoadout.CreateFromConfiguration(build ?? ConfigurationCatalog.AgileSkirmish, _catalog), _catalog, seed: 11);

    /// <summary>Wins the current encounter by destroying every enemy Core.</summary>
    private static void WinEncounter(MissionRun run)
    {
        var sim = run.BeginEncounter();
        foreach (var enemy in sim.Combatants.Where(c => c.Faction == Faction.Enemy))
            foreach (var core in enemy.Loadout.Installed.Where(e => e.Definition.IsCore))
                core.ApplyDamage(core.Definition.MaxDurability);
        sim.StepBeat();
        Assert.AreEqual(EncounterOutcome.PlayerVictory, sim.Outcome);
    }

    [TestMethod]
    public void Missions_UnlockByResearch()
    {
        var workshop = new WorkshopState();
        Assert.IsTrue(MissionRun.IsUnlocked(_catalog.Missions["mis_border_patrol"], workshop));
        Assert.IsFalse(MissionRun.IsUnlocked(_catalog.Missions["mis_quarry_siege"], workshop));

        workshop.AddSamples("mat_oathsteel", 2);
        workshop.Research(_catalog.Materials["mat_oathsteel"]);
        Assert.IsTrue(MissionRun.IsUnlocked(_catalog.Missions["mis_quarry_siege"], workshop));
        Assert.IsFalse(MissionRun.IsUnlocked(_catalog.Missions["mis_storm_reach"], workshop));
    }

    [TestMethod]
    public void FullMission_AlternatesEncountersAndRests_AndGrantsTheCompletionBonus()
    {
        var run = StartPatrol();
        var phases = new List<MissionPhase>();

        for (int stage = 0; stage < 3; stage++)
        {
            Assert.AreEqual(MissionPhase.Encounter, run.Phase);
            WinEncounter(run);
            var granted = run.FinishEncounter();
            Assert.IsNotEmpty(granted);
            phases.Add(run.Phase);
            if (run.Phase == MissionPhase.Rest)
            {
                Assert.AreEqual(15, run.RestBeatsRemaining);
                run.ContinueToNextEncounter();
            }
        }

        CollectionAssert.AreEqual(new[] { MissionPhase.Rest, MissionPhase.Rest, MissionPhase.Completed }, phases);
        var bonus = _catalog.Missions["mis_border_patrol"].CompletionRewards;
        foreach (var b in bonus)
            Assert.IsTrue(run.RewardsEarned.Any(r => r.MaterialId == b.MaterialId));
    }

    [TestMethod]
    public void Damage_AmmoAndMotes_CarryBetweenEncounters()
    {
        var run = StartPatrol();
        var vorpal = run.Loadout.Installed.First(e => e.Definition.Id == "eq_vorpal");

        WinEncounter(run);
        vorpal.ApplyDamage(1);
        run.Crest.Resources.AddMotes(2, run.Crest.Stats.MaxMotes);
        int motes = run.Crest.Resources.CurrentMotes;
        run.FinishEncounter();
        run.ContinueToNextEncounter();

        var sim = run.BeginEncounter();
        var crest = sim.FindCombatant(EncounterFactory.PlayerCombatantId)!;
        Assert.AreSame(run.Crest, crest);
        Assert.AreEqual(vorpal.Definition.MaxDurability - 1, vorpal.CurrentDurability, "No free repair between encounters.");
        Assert.AreEqual(motes, crest.Resources.CurrentMotes);
        Assert.AreEqual(run.CurrentEncounter.PlayerStart, crest.Position);
    }

    [TestMethod]
    public void Rest_TimeBuysMotes_SummonMakesFastIron_RepairsSpendIt()
    {
        var run = StartPatrol();
        WinEncounter(run);
        run.FinishEncounter();
        Assert.AreEqual(MissionPhase.Rest, run.Phase);

        var crest = run.Crest;
        crest.Resources.TrySpendMotes(crest.Resources.CurrentMotes);
        crest.Loadout.TryTakeCarriedFastIron(crest.Loadout.CarriedFastIron);
        Assert.AreEqual(5, run.MoteIntervalBeats);

        Assert.IsTrue(run.Wait().Succeeded);
        Assert.IsTrue(run.Wait().Succeeded);
        Assert.IsTrue(run.Wait().Succeeded);
        Assert.AreEqual(0, run.RestBeatsRemaining);
        Assert.AreEqual(3, crest.Resources.CurrentMotes);
        Assert.IsFalse(run.Wait().Succeeded, "Out of rest time.");
        Assert.IsFalse(run.SquareSummon().Succeeded, "Needs 4 Motes.");

        crest.Resources.AddMotes(1, crest.Stats.MaxMotes);
        Assert.IsTrue(run.SquareSummon().Succeeded);
        Assert.AreEqual(1, crest.TotalFastIron);
        Assert.AreEqual(0, crest.Resources.CurrentMotes);

        var talon = crest.Loadout.Installed.First(e => e.Definition.Id == "eq_talon");
        talon.ApplyDamage(talon.Definition.MaxDurability);
        Assert.IsTrue(talon.IsDisabled);
        Assert.IsTrue(run.Repair(talon.InstanceId).Succeeded);
        Assert.IsTrue(talon.IsOperational, "A disabled part comes back online.");
        Assert.AreEqual(0, crest.TotalFastIron);
        Assert.IsFalse(run.Repair(talon.InstanceId).Succeeded, "No fast-iron left.");
    }

    [TestMethod]
    public void Rest_ShapingAndCargo_Work()
    {
        var run = StartPatrol();
        WinEncounter(run);
        run.FinishEncounter();

        var crest = run.Crest;
        var vorpal = crest.Loadout.Installed.First(e => e.Definition.Id == "eq_vorpal");
        vorpal.ApplyDamage(vorpal.Definition.MaxDurability);

        var shaped = run.Shape(new ShapingOrder(ShapingKind.Component, "eq_vorpal"));
        Assert.IsTrue(shaped.Succeeded, shaped.Message);
        Assert.AreEqual("eq_vorpal", crest.Loadout.DamageOrder[^1].Definition.Id);
        Assert.IsFalse(run.Shape(new ShapingOrder(ShapingKind.Component, "eq_nonexistent")).Succeeded);
    }

    [TestMethod]
    public void Defeat_FailsTheMission_ButKeepsBankedRewards()
    {
        var run = StartPatrol();
        WinEncounter(run);
        var firstRewards = run.FinishEncounter();
        run.ContinueToNextEncounter();

        var sim = run.BeginEncounter();
        foreach (var core in run.Crest.Loadout.Installed.Where(e => e.Definition.IsCore))
            core.ApplyDamage(core.Definition.MaxDurability);
        sim.StepBeat();
        Assert.AreEqual(EncounterOutcome.PlayerDefeat, sim.Outcome);

        Assert.IsEmpty(run.FinishEncounter());
        Assert.AreEqual(MissionPhase.Failed, run.Phase);
        CollectionAssert.AreEqual(firstRewards.ToList(), run.RewardsEarned.ToList());
    }

    [TestMethod]
    public void Withdraw_DuringRest_EndsTheMissionKeepingRewards()
    {
        var run = StartPatrol();
        WinEncounter(run);
        run.FinishEncounter();
        int banked = run.RewardsEarned.Sum(r => r.Count);

        run.Withdraw();

        Assert.AreEqual(MissionPhase.Withdrawn, run.Phase);
        Assert.IsTrue(run.IsOver);
        Assert.AreEqual(banked, run.RewardsEarned.Sum(r => r.Count));
        Assert.IsFalse(run.Wait().Succeeded);
    }

    [TestMethod]
    public void NewEnemies_ReactWithTheirOwnRules()
    {
        var warden = _catalog.Enemies["enemy_warden"];
        Assert.IsNotEmpty(warden.Assembly.ReactionRules);

        var player = TestHelpers.CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(5, 5));
        var enemy = TestHelpers.CreateEnemy("enemy_warden", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        TestHelpers.StepUntil(sim, () => player.State == ActionState.Executing);
        sim.StepBeat();

        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.ReactionRuleTriggered && e.ActorId == enemy.Id));
    }

    [TestMethod]
    public void Validator_RejectsBrokenMissions()
    {
        var missions = _catalog.Missions.ToDictionary();
        missions["mis_bad"] = new MissionDefinition
        {
            Id = "mis_bad",
            Name = "Bad",
            EncounterIds = ["enc_missing"],
            RequiredResearch = ["mat_missing"],
            RestBeats = -1
        };
        var catalog = new GameCatalog(_catalog.Actions, _catalog.Equipment, _catalog.StarterConfigurations, _catalog.Enemies,
            _catalog.Rules, _catalog.Materials, _catalog.Encounters, _catalog.Consumables, missions);

        var errors = ContentValidator.Validate(catalog);

        Assert.IsTrue(errors.Any(e => e.Contains("unknown encounter 'enc_missing'")));
        Assert.IsTrue(errors.Any(e => e.Contains("requires unknown material 'mat_missing'")));
        Assert.IsTrue(errors.Any(e => e.Contains("restBeats cannot be negative")));
    }
}
