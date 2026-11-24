using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

/// <summary>Allied pilots: content, encounter setup, outcome, AI targeting, and carrying allies through a mission.</summary>
[TestClass]
public sealed class AllyTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    private static string[] Lance => ["pilot_marrow", "pilot_vell"];

    private CombatSimulation Fight(string encounterId = EncounterFactory.TestEncounterId, int seed = 3) =>
        EncounterFactory.CreateEncounter(_catalog.Encounters[encounterId],
            CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog), seed, _catalog,
            EncounterFactory.CreateAllies(Lance, _catalog));

    private static void DestroyCore(Combatant c)
    {
        foreach (var core in c.Loadout.Installed.Where(e => e.Definition.IsCore))
            core.ApplyDamage(core.Definition.MaxDurability);
    }

    [TestMethod]
    public void ShippedPilots_LoadAndRoundTrip()
    {
        Assert.IsGreaterThanOrEqualTo(2, _catalog.Pilots.Count);
        Assert.AreEqual("Anvil", _catalog.Pilots["pilot_marrow"].DisplayName);

        var dir = Path.Combine(Path.GetTempPath(), "tnt_pilots_" + Guid.NewGuid().ToString("N"));
        try
        {
            ContentSerializer.SaveToDirectory(_catalog, dir);
            var reloaded = ContentSerializer.LoadFromDirectory(dir);
            CollectionAssert.AreEquivalent(_catalog.Pilots.Keys.ToList(), reloaded.Pilots.Keys.ToList());
            CollectionAssert.AreEqual(_catalog.Pilots["pilot_vell"].Assembly.ComponentIds.ToList(),
                reloaded.Pilots["pilot_vell"].Assembly.ComponentIds.ToList());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Allies_JoinThePlayerFaction_AtFlankingStarts()
    {
        var sim = Fight();
        var enc = _catalog.Encounters[EncounterFactory.TestEncounterId];
        var marrow = sim.FindCombatant(EncounterFactory.AllyCombatantId("pilot_marrow"))!;
        var vell = sim.FindCombatant(EncounterFactory.AllyCombatantId("pilot_vell"))!;

        Assert.AreEqual(Faction.Player, marrow.Faction);
        Assert.AreEqual("Anvil", marrow.Name);
        Assert.AreEqual(enc.AllyStart(0), marrow.Position);
        Assert.AreEqual(enc.AllyStart(1), vell.Position);
        Assert.AreNotEqual(marrow.Position, vell.Position);
        Assert.AreNotEqual(enc.PlayerStart, marrow.Position);
    }

    [TestMethod]
    public void TheFightIsLost_WhenThePlayersCrestFalls_EvenWithAlliesStanding()
    {
        var sim = Fight();
        DestroyCore(sim.FindCombatant(EncounterFactory.PlayerCombatantId)!);
        sim.StepBeat();
        Assert.AreEqual(EncounterOutcome.PlayerDefeat, sim.Outcome);
    }

    [TestMethod]
    public void LosingAllies_DoesNotLoseTheFight()
    {
        var sim = Fight();
        foreach (var ally in sim.Combatants.Where(AllyAIController.IsAlly))
            DestroyCore(ally);
        sim.StepBeat();
        Assert.AreEqual(EncounterOutcome.InProgress, sim.Outcome);
    }

    [TestMethod]
    public void Enemies_TargetTheNearestLanceMember_AndDontFlipFlop()
    {
        var sim = Fight();
        var ai = new EnemyAIController();
        var enemy = sim.Combatants.First(c => c.Faction == Faction.Enemy);
        var lance = sim.Combatants.Where(c => c.Faction == Faction.Player).ToList();
        var nearest = lance.OrderBy(c => c.Position.DistanceTo(enemy.Position)).First();

        Assert.AreEqual(nearest.Id, ai.ChooseTarget(enemy, lance).Id);

        // Another Crest a little closer than the current target is not enough to switch...
        var other = lance.First(c => c != nearest);
        var toEnemy = (enemy.Position - nearest.Position).Normalized();
        other.PrepareForEncounter(nearest.Position + toEnemy * 1.0);
        Assert.AreEqual(nearest.Id, ai.ChooseTarget(enemy, lance).Id);

        // ...but one clearly closer is.
        other.PrepareForEncounter(enemy.Position - toEnemy * 0.5);
        Assert.AreEqual(other.Id, ai.ChooseTarget(enemy, lance).Id);
    }

    [TestMethod]
    public void Allies_FightOnTheirOwn()
    {
        var sim = Fight();
        var allyAI = new AllyAIController();
        var enemyAI = new EnemyAIController();
        while (sim.Outcome == EncounterOutcome.InProgress && sim.CurrentBeat < 300)
        {
            enemyAI.Update(sim);
            allyAI.Update(sim); // the player's own Crest stands idle
            sim.StepBeat();
        }

        var allyIds = sim.Combatants.Where(AllyAIController.IsAlly).Select(a => a.Id).ToHashSet();
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.ActionExecuted && allyIds.Contains(e.ActorId)),
            "allies should attack without orders");
        // The player's own reaction rules still fire; nothing else may.
        Assert.IsFalse(sim.Events.Any(e => e.Type == CombatEventType.ActionStarted && e.ActorId == EncounterFactory.PlayerCombatantId &&
                                           e.ActionId is { } id && !_catalog.Actions[id].IsReactive),
            "the ally AI must not drive the player's Crest");
    }

    [TestMethod]
    public void Mission_CarriesAlliesThrough_ADownedAllySitsOut_UntilRepaired()
    {
        var run = MissionRun.Start(_catalog.Missions["mis_border_patrol"],
            CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog), _catalog, seed: 5,
            allies: EncounterFactory.CreateAllies(Lance, _catalog));
        var marrow = run.Allies[0];

        var sim = run.BeginEncounter();
        Assert.AreEqual(3, sim.Combatants.Count(c => c.Faction == Faction.Player));
        DestroyCore(marrow);
        foreach (var enemy in sim.Combatants.Where(c => c.Faction == Faction.Enemy))
            DestroyCore(enemy);
        sim.StepBeat();
        Assert.AreEqual(EncounterOutcome.PlayerVictory, sim.Outcome);
        run.FinishEncounter();
        Assert.AreEqual(MissionPhase.Rest, run.Phase);

        // Downed: can't summon or shape, but can be repaired, with its own fast-iron.
        Assert.IsFalse(run.SquareSummon(marrow.Id).Succeeded);
        var core = marrow.Loadout.Installed.First(e => e.Definition.IsCore);
        Assert.IsFalse(run.Repair(core.InstanceId, marrow.Id).Succeeded, "no fast-iron yet");
        Assert.IsFalse(run.Repair(core.InstanceId).Succeeded, "the part belongs to the ally, not the player");

        run.ContinueToNextEncounter();
        sim = run.BeginEncounter();
        Assert.IsNull(sim.FindCombatant(marrow.Id), "a downed ally sits the next fight out");
        Assert.IsNotNull(sim.FindCombatant(run.Allies[1].Id));
        foreach (var enemy in sim.Combatants.Where(c => c.Faction == Faction.Enemy))
            DestroyCore(enemy);
        sim.StepBeat();
        run.FinishEncounter();

        marrow.Resources.AddFastIron(core.Definition.MaxDurability);
        while (core.IsDisabled)
            Assert.IsTrue(run.Repair(core.InstanceId, marrow.Id).Succeeded);
        Assert.IsFalse(marrow.IsDefeated);
        Assert.IsTrue(run.RestLog.Any(e => e.ActorId == marrow.Id && e.Message.StartsWith("Anvil: Repaired")));

        run.ContinueToNextEncounter();
        Assert.IsNotNull(run.BeginEncounter().FindCombatant(marrow.Id), "repaired, the ally rejoins");
    }

    [TestMethod]
    public void Resting_GivesEveryStandingCrestAMote()
    {
        var run = MissionRun.Start(_catalog.Missions["mis_border_patrol"],
            CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog), _catalog, seed: 5,
            allies: EncounterFactory.CreateAllies(Lance, _catalog));
        var sim = run.BeginEncounter();
        foreach (var enemy in sim.Combatants.Where(c => c.Faction == Faction.Enemy))
            DestroyCore(enemy);
        sim.StepBeat();
        run.FinishEncounter();

        var before = run.Lance.ToDictionary(c => c.Id, c => c.Resources.CurrentMotes);
        Assert.IsTrue(run.Wait().Succeeded);
        foreach (var crest in run.Lance.Where(c => before[c.Id] < c.Stats.MaxMotes))
            Assert.AreEqual(before[crest.Id] + 1, crest.Resources.CurrentMotes, crest.Name);
    }

    [TestMethod]
    public void ALance_HoldsAtMostTwoDistinctAllies()
    {
        var mission = _catalog.Missions["mis_border_patrol"];
        CrestLoadout Player() => CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var marrow = EncounterFactory.CreateAlly(_catalog.Pilots["pilot_marrow"], _catalog);
        var vell = EncounterFactory.CreateAlly(_catalog.Pilots["pilot_vell"], _catalog);
        var extra = EncounterFactory.CreateAlly(_catalog.Pilots["pilot_vell"] with { Id = "pilot_extra" }, _catalog);

        Assert.ThrowsExactly<ArgumentException>(() => MissionRun.Start(mission, Player(), _catalog, 1, allies: [marrow, vell, extra]));
        Assert.ThrowsExactly<ArgumentException>(() => MissionRun.Start(mission, Player(), _catalog, 1, allies: [marrow, marrow]));
        Assert.AreEqual(2, MissionRun.Start(mission, Player(), _catalog, 1, allies: [marrow, vell]).Allies.Count);
    }

    [TestMethod]
    public void Profile_SavesPilotCrestsAndLance_AndDropsUnknownPilots()
    {
        var refit = _catalog.Pilots["pilot_vell"].Assembly with
        {
            ComponentIds = [.. _catalog.Pilots["pilot_vell"].Assembly.ComponentIds, "armor_heavy_plate", "eq_gone"]
        };
        var profile = new Core.Workshop.PlayerProfile
        {
            Assemblies = [ConfigurationCatalog.AgileSkirmish],
            Pilots =
            [
                new Core.Workshop.PilotRecord { PilotId = "pilot_vell", Assembly = refit },
                new Core.Workshop.PilotRecord { PilotId = "pilot_retired", Assembly = refit }
            ],
            Lance = ["pilot_vell", "pilot_retired", "pilot_marrow"]
        };

        var loaded = Core.Workshop.ProfileSerializer.Deserialize(Core.Workshop.ProfileSerializer.Serialize(profile), _catalog);

        var vell = loaded.Profile.Pilots.Single();
        Assert.AreEqual("pilot_vell", vell.PilotId);
        CollectionAssert.Contains(vell.Assembly.ComponentIds.ToList(), "armor_heavy_plate");
        CollectionAssert.DoesNotContain(vell.Assembly.ComponentIds.ToList(), "eq_gone");
        CollectionAssert.AreEqual(new[] { "pilot_vell", "pilot_marrow" }, loaded.Profile.Lance.ToList());
        Assert.IsTrue(loaded.Warnings.Any(w => w.Contains("pilot_retired")));
    }

    [TestMethod]
    public void BalanceRunner_PlaysWithAllies_Deterministically()
    {
        var runner = new BalanceRunner(_catalog, Lance);
        var a = runner.RunOne(ConfigurationCatalog.AgileSkirmish, seed: 9, maxBeats: 300);
        var b = runner.RunOne(ConfigurationCatalog.AgileSkirmish, seed: 9, maxBeats: 300);
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(EncounterOutcome.InProgress, a.Outcome);
    }
}
