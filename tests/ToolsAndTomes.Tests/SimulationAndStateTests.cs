using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class SimulationAndStateTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void ActionStateTransitions_FollowMovingWindupExecutingRecoveryIdle()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_skirmisher", new Vec2(12, 5));
        var sim = new CombatSimulation([player, enemy], seed: 42);

        var result = sim.IssueCommand(new UseActionCommand(player.Id, "weapon_talon", enemy.Id));
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(ActionState.Idle, player.State, "Commands are buffered until the beat boundary.");

        var observedStates = new List<ActionState>();
        for (int i = 0; i < 20; i++)
        {
            sim.StepBeat();
            if (observedStates.Count == 0 || observedStates[^1] != player.State)
                observedStates.Add(player.State);

            if (player.State == ActionState.Idle)
                break;
        }

        CollectionAssert.AreEqual(
            new[] { ActionState.Moving, ActionState.Windup, ActionState.Executing, ActionState.Recovery, ActionState.Idle },
            observedStates);
    }

    [TestMethod]
    public void CommandsIssuedMidBeat_TakeEffectAtNextBeat_AndMoveWholeSpeedPerBeat()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 3);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(20, 5)));
        Assert.AreEqual(ActionState.Idle, player.State);
        Assert.AreEqual(new Vec2(2, 5), player.Position);

        sim.StepBeat(); // command takes effect for the next beat
        Assert.AreEqual(ActionState.Moving, player.State);
        Assert.AreEqual(new Vec2(2, 5), player.Position);
        Assert.AreEqual(new Vec2(2 + player.Stats.MoveSpeed, 5), sim.ProjectEndOfBeatPosition(player));

        sim.StepBeat(); // one full beat of movement
        Assert.AreEqual(2.0 + player.Stats.MoveSpeed, player.Position.X, 1e-9);
    }

    [TestMethod]
    public void PauseAndResume_FreezesSimulationClockAndPositions()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 7);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(15, 5)));
        sim.StepBeat();
        int beatBeforePause = sim.CurrentBeat;
        var posBeforePause = player.Position;

        sim.Pause();
        for (int i = 0; i < 20; i++)
            sim.StepBeat();

        Assert.AreEqual(beatBeforePause, sim.CurrentBeat);
        Assert.AreEqual(posBeforePause, player.Position);

        sim.Resume();
        sim.StepBeat();
        Assert.AreEqual(beatBeforePause + 1, sim.CurrentBeat);
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
    public void WindupImpactRecoveryBeats_MatchActionDefinition()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(4, 5));
        var sim = new CombatSimulation([player, enemy], seed: 12);

        var titan = _catalog.GetAction("weapon_titan");
        sim.IssueCommand(new UseActionCommand(player.Id, titan.Id, enemy.Id));
        sim.StepBeat();
        Assert.AreEqual(ActionState.Windup, player.State);

        int windup = 0, impact = 0, recovery = 0;
        for (int i = 0; i < 20 && player.State != ActionState.Idle; i++)
        {
            if (player.State == ActionState.Windup) windup++;
            else if (player.State == ActionState.Executing) impact++;
            else if (player.State == ActionState.Recovery) recovery++;

            sim.StepBeat();
        }

        Assert.AreEqual(titan.WindupBeats, windup);
        Assert.AreEqual(titan.ImpactBeats, impact);
        Assert.AreEqual(titan.RecoveryBeats, recovery);
    }

    [TestMethod]
    public void Cooldown_CountsWholeBeatsAfterRecovery_AndQueuedCommandWaitsForIt()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(8, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));
        var talon = _catalog.GetAction("weapon_talon");

        sim.IssueCommand(new UseActionCommand(player.Id, talon.Id, enemy.Id));
        StepUntil(sim, () => player.State == ActionState.Recovery);

        // Queue the same attack during recovery; it must wait out the cooldown.
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, talon.Id, enemy.Id)).IsValid);

        int idleBeats = 0;
        sim.StepBeat(); // recovery ends
        while (player.State == ActionState.Idle && idleBeats < 10)
        {
            idleBeats++;
            sim.StepBeat();
        }

        Assert.AreEqual(talon.CooldownBeats, idleBeats);
        Assert.AreEqual(ActionState.Windup, player.State);
    }

    [TestMethod]
    public void ActionDefinitions_RespectTabletopTimingRules()
    {
        foreach (var action in _catalog.Actions.Values)
        {
            Assert.IsGreaterThanOrEqualTo(1, action.ImpactBeats, $"{action.Id} must have an impact window.");
            Assert.IsGreaterThanOrEqualTo(0, action.WindupBeats);
            Assert.IsGreaterThanOrEqualTo(0, action.RecoveryBeats);
            if (action.IsReactive)
                Assert.AreEqual(0, action.WindupBeats, $"Reaction {action.Id} must have no windup.");
        }
    }

    private (Vec2 Position, List<string> Events, List<int> Durabilities) RunDeterministicScenario(int seed)
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(3, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(5, 5));
        var sim = new CombatSimulation([player, enemy], seed);

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_titan", enemy.Id));
        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));

        for (int i = 0; i < 20; i++)
            sim.StepBeat();

        var durs = enemy.Loadout.Installed.Select(e => e.CurrentDurability).ToList();
        return (enemy.Position, sim.Events.Select(e => $"{e.Beat}:{e.Type}:{e.Message}").ToList(), durs);
    }
}
