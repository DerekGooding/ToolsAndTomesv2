using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class BalanceRunnerTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void BalanceRun_IsDeterministic_AndSummarizesEveryBuild()
    {
        var runner = new BalanceRunner(_catalog);
        var builds = _catalog.StarterConfigurations.Values.ToList();

        var first = runner.Run(builds, seeds: 5, maxBeats: 200);
        var second = runner.Run(builds, seeds: 5, maxBeats: 200);

        CollectionAssert.AreEqual(first.ToList(), second.ToList());

        var summary = BalanceRunner.Summarize(first, _catalog);
        Assert.HasCount(builds.Count, summary);
        Assert.IsTrue(summary.All(s => s.Runs == 5 && s.Wins + s.Losses + s.Timeouts == 5));
        StringAssert.Contains(BalanceRunner.FormatTable(summary), "Agile Skirmish Assembly");
    }

    [TestMethod]
    public void AutoPilot_FightsWithTheBuild_AndRespectsWit()
    {
        var result = new BalanceRunner(_catalog).RunOne(ConfigurationCatalog.AgileSkirmish, seed: 3, maxBeats: 200);

        Assert.IsGreaterThan(0, result.Beats);
        Assert.IsTrue(result.EnemiesDefeated > 0 || result.Outcome != EncounterOutcome.PlayerVictory);

        // Re-run one encounter by hand to confirm the pilot never triggers Wit rejections.
        var loadout = Core.Instances.CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var sim = EncounterFactory.CreateTestEncounter(loadout, seed: 3, _catalog);
        var player = sim.Combatants.First(c => c.Faction == Faction.Player);
        var pilot = new AutoPilot();
        var enemies = new EnemyAIController();
        for (int i = 0; i < 60 && sim.Outcome == EncounterOutcome.InProgress; i++)
        {
            enemies.Update(sim);
            pilot.Update(sim, player);
            sim.StepBeat();
        }

        Assert.IsTrue(sim.Events.Any(e => e.ActorId == player.Id && e.Type == CombatEventType.ActionExecuted));
        Assert.IsFalse(sim.Events.Any(e => e.ActorId == player.Id && e.Type == CombatEventType.CommandRejected && e.Message.Contains("Wit")));
    }
}
