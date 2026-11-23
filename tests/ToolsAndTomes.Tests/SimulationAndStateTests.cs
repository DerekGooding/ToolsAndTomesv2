using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class SimulationAndStateTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void ActionStateTransitions_FollowMovingWindupExecutingRecoveryIdle()
    {
        var player = CreatePlayer(TemplateCatalog.Strider, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_skirmisher", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], seed: 42);

        // Vorpal range is 2.0, enemy is 5.0m away -> should start Moving first
        var result = sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(ActionState.Moving, player.State);

        var observedStates = new List<ActionState> { player.State };
        for (int i = 0; i < 120; i++)
        {
            sim.StepTick();
            if (observedStates[^1] != player.State)
                observedStates.Add(player.State);

            if (observedStates.Count >= 5 && player.State == ActionState.Idle)
                break;
        }

        CollectionAssert.AreEqual(
            new[] { ActionState.Moving, ActionState.Windup, ActionState.Executing, ActionState.Recovery, ActionState.Idle },
            observedStates);
    }

    [TestMethod]
    public void PauseAndResume_FreezesSimulationClockAndPositions()
    {
        var player = CreatePlayer(TemplateCatalog.Strider, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(10, 5));
        var sim = new CombatSimulation([player, enemy], seed: 7);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(8, 5)));
        sim.StepTick();
        int tickBeforePause = sim.CurrentTick;
        var posBeforePause = player.Position;

        sim.Pause();
        for (int i = 0; i < 20; i++)
            sim.StepTick();

        Assert.AreEqual(tickBeforePause, sim.CurrentTick);
        Assert.AreEqual(posBeforePause, player.Position);

        sim.Resume();
        sim.StepTick();
        Assert.AreEqual(tickBeforePause + 1, sim.CurrentTick);
        Assert.IsGreaterThan(posBeforePause.X, player.Position.X);
    }

    [TestMethod]
    public void IdenticalSeedsAndCommands_ProduceIdenticalSimulationOutcomes()
    {
        var (posA, eventsA, durA) = RunDeterministicScenario(seed: 999);
        var (posB, eventsB, durB) = RunDeterministicScenario(seed: 999);

        Assert.AreEqual(posA, posB);
        Assert.HasCount(eventsA.Count, eventsB);
        CollectionAssert.AreEqual(eventsA, eventsB);
        CollectionAssert.AreEqual(durA, durB);
    }

    [TestMethod]
    public void WindupExecutionRecoveryTiming_MatchesActionDefinition()
    {
        var player = CreatePlayer(TemplateCatalog.Strider, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(4, 5)); // In range of Vorpal (2.0)
        var sim = new CombatSimulation([player, enemy], seed: 12);

        var vorpal = _catalog.GetAction("weapon_vorpal");
        sim.IssueCommand(new UseActionCommand(player.Id, vorpal.Id, enemy.Id));
        Assert.AreEqual(ActionState.Windup, player.State);

        int windupTicks = 0, execTicks = 0, recTicks = 0;
        for (int i = 0; i < 40; i++)
        {
            if (player.State == ActionState.Windup) windupTicks++;
            else if (player.State == ActionState.Executing) execTicks++;
            else if (player.State == ActionState.Recovery) recTicks++;
            else if (player.State == ActionState.Idle) break;

            sim.StepTick();
        }

        Assert.AreEqual(vorpal.WindupTicks, windupTicks);
        Assert.AreEqual(vorpal.ExecutionTicks, execTicks);
        Assert.AreEqual(vorpal.RecoveryTicks, recTicks);
    }

    private (Vec2 Position, List<string> Events, List<int> Durabilities) RunDeterministicScenario(int seed)
    {
        var player = CreatePlayer(TemplateCatalog.Bulwark, new Vec2(3, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(5, 5));
        var sim = new CombatSimulation([player, enemy], seed);

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_titan", enemy.Id));
        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));

        for (int i = 0; i < 80; i++)
            sim.StepTick();

        var durs = enemy.Loadout.Installed.Select(e => e.CurrentDurability).ToList();
        return (enemy.Position, sim.Events.Select(e => $"{e.Tick}:{e.Type}:{e.Message}").ToList(), durs);
    }

    private Combatant CreatePlayer(CrestTemplateDefinition tpl, Vec2 pos) =>
        new("player", tpl.Name, Faction.Player, CrestLoadout.CreateFromTemplate(tpl, _catalog, "p"), pos, startingMotes: 2);

    private Combatant CreateEnemy(string enemyId, Vec2 pos)
    {
        var def = _catalog.Enemies[enemyId];
        return new Combatant(enemyId, def.Name, Faction.Enemy, CrestLoadout.CreateFromTemplate(def.Template, _catalog, "e"), pos, startingMotes: 2, def.Archetype, def.PreferredRange);
    }
}
