using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class ConcurrentActionTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void DifferentComponents_RunActionsAtTheSameTime_EachReservingWit()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_talon", enemy.Id)).IsValid);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id)).IsValid);
        sim.StepBeat();

        Assert.HasCount(2, player.ActionsInMotion);
        Assert.IsTrue(player.ActionsInMotion.All(a => a.Phase == ActionState.Windup));
        Assert.AreEqual(2, player.Resources.ReservedWit);
        Assert.AreNotEqual(player.ActionsInMotion[0].SourceEquipmentInstanceId, player.ActionsInMotion[1].SourceEquipmentInstanceId);
    }

    [TestMethod]
    public void AvailableWit_LimitsHowManyActionsCanBeCommitted()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));
        Assert.AreEqual(4, player.Stats.AvailableWit(0));
        player.Resources.ReserveWit("sustained_elsewhere", 2); // leaves 2 Wit to commit

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_talon", enemy.Id));   // 1
        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));  // 1
        sim.IssueCommand(new UseActionCommand(player.Id, "spell_push", enemy.Id));     // 1: over budget
        sim.StepBeat();

        Assert.HasCount(2, player.ActionsInMotion);
        Assert.IsFalse(player.ActionsInMotion.Any(a => a.Definition.Id == "spell_push"));
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected && e.Message.Contains("Wit")));
    }

    [TestMethod]
    public void SameComponent_RunsOneActionAtATime_SecondCommandWaits()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));

        sim.IssueCommand(new UseActionCommand(player.Id, "spell_push", enemy.Id));
        sim.StepBeat();
        // Pull uses the same Spellbook component as Push.
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "spell_pull", enemy.Id)).IsValid);
        sim.StepBeat();

        Assert.HasCount(1, player.ActionsInMotion);
        Assert.AreEqual("spell_push", player.ActionsInMotion[0].Definition.Id);
        Assert.IsTrue(player.HasQueuedCommands);

        StepUntil(sim, () => player.ActionsInMotion.Any(a => a.Definition.Id == "spell_pull"));
    }

    [TestMethod]
    public void Interruption_StopsEveryInterruptibleAction_AndLocksTheirComponents()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var vorpalComp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_vorpal");
        var talonComp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_talon");
        var bookComp = player.Loadout.Installed.First(e => e.Definition.IsTome);

        var vorpal = player.AddAction(_catalog.GetAction("weapon_vorpal"), vorpalComp.InstanceId, "x", null);
        vorpal.Phase = ActionState.Windup;
        vorpal.PhaseBeatsRemaining = 1;

        var push = player.AddAction(_catalog.GetAction("spell_push"), bookComp.InstanceId, "x", null);
        push.Phase = ActionState.Windup;
        push.PhaseBeatsRemaining = 1;

        var steadyShot = _catalog.GetAction("weapon_talon") with { IsUninterruptible = true };
        var talon = player.AddAction(steadyShot, talonComp.InstanceId, "x", null);
        talon.Phase = ActionState.Windup;
        talon.PhaseBeatsRemaining = 1;

        var hit = new IncomingHit("src", "Hammer", player.Id, new Vec2(6, 5), DamagePoints: 0, PushDistance: 0, InterruptionPower: 5);
        new DamageResolutionService().ResolveActorBatch(player, [hit], 0, new FixedRandom(6), 24, 14, []);

        CollectionAssert.AreEqual(new[] { talon }, player.ActionsInMotion.ToList(), "Only the uninterruptible action survives.");
        Assert.IsTrue(player.ComponentLockouts.ContainsKey(vorpalComp.InstanceId));
        Assert.IsTrue(player.ComponentLockouts.ContainsKey(bookComp.InstanceId));
        Assert.IsFalse(player.ComponentLockouts.ContainsKey(talonComp.InstanceId));
        Assert.IsTrue(player.IsComponentBusy(vorpalComp.InstanceId));
    }

    [TestMethod]
    public void Walking_RunsAlongsideASpellWindup()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(10, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));

        sim.IssueCommand(new UseActionCommand(player.Id, "spell_push", enemy.Id));
        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(4, 12)));
        sim.StepBeat();

        Assert.IsNotNull(player.Movement);
        Assert.AreEqual(ActionState.Windup, player.ActionsInMotion.Single().Phase);

        sim.StepBeat();
        Assert.IsGreaterThan(5.0, player.Position.Y, "Moved while the spell wound up.");
        Assert.AreEqual(ActionState.Executing, player.ActionsInMotion.Single().Phase);
    }

    [TestMethod]
    public void WithoutOperationalLocomotion_TheCrestCannotWalk()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var legs = player.Loadout.Installed.First(e => e.Definition.IsLocomotion);
        legs.ApplyDamage(legs.Definition.MaxDurability);
        var sim = new CombatSimulation([player, enemy], seed: 2);

        Assert.IsFalse(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id)).IsValid);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(10, 5)));
        sim.StepBeat();
        sim.StepBeat();

        Assert.AreEqual(new Vec2(4, 5), player.Position);
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected && e.Message.Contains("locomotion")));
    }

    [TestMethod]
    public void LocomotionWithAWitCost_ReservesWitWhileWalking()
    {
        var striders = new EquipmentDefinition
        {
            Id = "test_arcane_striders",
            Name = "Arcane Striders",
            Description = "Test legs that cost Wit to walk.",
            Category = ComponentCategory.Mobility,
            MaxDurability = 2,
            MoveSpeedBonus = 4,
            IsLocomotion = true,
            MovementWitCost = 2
        };
        var loadout = new CrestLoadout("l", "Strider Test",
        [
            new EquipmentInstance("legs", striders),
            new EquipmentInstance("core", _catalog.GetEquipment("core_aether_dynamo"))
        ]);
        var player = new Combatant("player", "P", Faction.Player, loadout, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 2);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(6, 5)));
        sim.StepBeat();
        Assert.AreEqual(2, player.Resources.ReservedWit);

        sim.StepBeat(); // arrives (4 units)
        Assert.IsNull(player.Movement);
        Assert.AreEqual(0, player.Resources.ReservedWit);
    }

    [TestMethod]
    public void JetDash_MovesUpToItsDistanceTowardThePoint()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 2);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "mobility_dash", TargetPoint: new Vec2(4, 13))).IsValid);
        StepUntil(sim, () => player.Position.Y > 5);

        Assert.AreEqual(9.0, player.Position.Y, 1e-9);
    }
}
