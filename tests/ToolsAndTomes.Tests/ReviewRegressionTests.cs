using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

/// <summary>Regression tests for issues found in code review.</summary>
[TestClass]
public sealed class ReviewRegressionTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void PlainMove_AbandonsApproachingAttacks_WithANotice()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(2, 7));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 7));
        var sim = new CombatSimulation([player, enemy], seed: 1);

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        sim.StepBeat();
        Assert.AreEqual(ActionState.Moving, player.ActionsInMotion.Single().Phase);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(2, 1)));
        sim.StepBeat();

        Assert.IsEmpty(player.ActionsInMotion, "The approach no longer hangs on with nothing walking it in.");
        Assert.IsNull(player.Movement?.ForActionId);
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected && e.Message.Contains("approach abandoned")));
    }

    [TestMethod]
    public void TwoAttacksOnTheSameTarget_ShareOneWalk_AndBothConnect()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(2, 7));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(16, 7));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_talon", enemy.Id)); // range 8
        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id)); // range 2
        sim.StepBeat();
        Assert.HasCount(2, player.ActionsInMotion);
        Assert.AreEqual(2.0, player.Movement!.StopDistance, 1e-9, "Walks until the shorter range is reached.");

        StepUntil(sim, () => player.ActionsInMotion.All(a => a.Phase != ActionState.Moving) && player.ActionsInMotion.Count == 0, maxBeats: 20);
        Assert.AreEqual(2, sim.Events.Count(e => e.Type == CombatEventType.ActionExecuted && e.ActorId == player.Id));
        Assert.IsFalse(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected && e.ActorId == player.Id));
    }

    [TestMethod]
    public void InterruptedCommittedAction_StillGoesOnCooldown()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var talonComp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_talon");
        var talon = _catalog.GetAction("weapon_talon");
        var act = player.AddAction(talon, talonComp.InstanceId, "x", null);
        act.Phase = ActionState.Executing;
        act.PhaseBeatsRemaining = 1;
        act.CostsCommitted = true;

        var hit = new IncomingHit("src", "Hammer", player.Id, new Vec2(6, 5), 0, 0, InterruptionPower: 5);
        new DamageResolutionService().ResolveActorBatch(player, [hit], 0, new FixedRandom(6), 24, 14, []);

        Assert.IsEmpty(player.ActionsInMotion);
        Assert.AreEqual(talon.CooldownBeats, player.GetCooldown(talon.Id));
    }

    [TestMethod]
    public void WitCollapse_SkipsActionsThatHoldNoWit()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(4, 5));
        var titanComp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_titan");
        var breathComp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_dragons_breath");

        var holding = player.AddAction(_catalog.GetAction("weapon_titan"), titanComp.InstanceId, "x", null);
        holding.Phase = ActionState.Windup;
        player.Resources.ReserveWit(holding.WitReservationKey, 2);

        var recovering = player.AddAction(_catalog.GetAction("weapon_dragons_breath"), breathComp.InstanceId, "x", null);
        recovering.Phase = ActionState.Recovery; // newer, but its Wit was released at impact

        player.Resources.ReserveWit("pressure", player.Stats.AvailableWit(player.Resources.ReservedWit) + 1);
        var dropped = player.EnforceWitBudget();

        CollectionAssert.AreEqual(new[] { "Titan Cleave" }, dropped);
        CollectionAssert.Contains(player.ActionsInMotion.ToList(), recovering);
    }

    [TestMethod]
    public void TwoReactionsInOneBeat_CannotSpendMoreWitThanAvailable()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5));
        player.Loadout.Install(new EquipmentInstance("blink", _catalog.GetEquipment("eq_blink_drive")));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);

        // Leave exactly 1 Wit: enough for one 1-Wit reaction, not two.
        int spare = player.Stats.AvailableWit(player.Resources.ReservedWit);
        player.Resources.ReserveWit("pressure", spare - 1);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "defense_repulsor", enemy.Id)).IsValid);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "defense_blink", TargetPoint: new Vec2(1, 5))).IsValid);
        sim.StepBeat();

        int reacted = sim.Events.Count(e => e.Type == CombatEventType.ActionStarted && e.ActorId == player.Id && e.Message.Contains("reacts"));
        Assert.AreEqual(1, reacted);
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected && e.ActorId == player.Id && e.Message.Contains("Wit")));
    }

    [TestMethod]
    public void IronWard_ReinforcesAnActiveAegis_InsteadOfReplacingIt()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        player.ActivateAegis(2, 4, 2);
        player.AegisBeatsRemaining = 3;

        player.ActivateAegis(1, 1, 0);

        Assert.AreEqual(3, player.AegisBlocksRemaining);
        Assert.AreEqual(3, player.AegisBeatsRemaining);
        Assert.AreEqual(2, player.Resources.GetReservation(Combatant.AegisWitReservationKey));
    }

    [TestMethod]
    public void ShapingTheSamePartTwice_InstallsTwoDistinctComponents()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var plan = new ShapingRules.ShapingPlan(0, _catalog.GetEquipment("eq_talon"), null, null);
        int before = player.Loadout.Installed.Count;

        ShapingRules.Apply(player, plan, "same_prefix", 0, []);
        ShapingRules.Apply(player, plan, "same_prefix", 0, []);

        Assert.AreEqual(before + 2, player.Loadout.Installed.Count);
        Assert.AreEqual(player.Loadout.Installed.Count, player.Loadout.Installed.Select(e => e.InstanceId).Distinct().Count());
    }

    [TestMethod]
    public void MovementWit_IsKeptWhenANewWalkCannotBeAfforded()
    {
        var striders = _catalog.GetEquipment("mob_skystrider_legs"); // 1 Wit to walk
        var loadout = new CrestLoadout("l", "Test",
        [
            new EquipmentInstance("legs", striders),
            new EquipmentInstance("core", _catalog.GetEquipment("core_aether_dynamo"))
        ]);
        var player = new Combatant("player", "P", Faction.Player, loadout, new Vec2(2, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 2);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(20, 5)));
        sim.StepBeat();
        Assert.AreEqual(1, player.Resources.GetReservation(Combatant.MovementWitReservationKey));

        // Spend every other point of Wit, then order a different walk: it is still affordable (the old walk's Wit
        // counts as freed), and the reservation never drops to zero while walking.
        player.Resources.ReserveWit("pressure", player.Stats.AvailableWit(player.Resources.ReservedWit));
        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(2, 12)));
        sim.StepBeat();

        Assert.IsNotNull(player.Movement);
        Assert.AreEqual(1, player.Resources.GetReservation(Combatant.MovementWitReservationKey));
        Assert.IsGreaterThanOrEqualTo(0, player.Stats.AvailableWit(player.Resources.ReservedWit));
    }

    [TestMethod]
    public void SavedProfiles_AlwaysRecordTheirVersion()
    {
        var json = Core.Workshop.ProfileSerializer.Serialize(new Core.Workshop.PlayerProfile());
        StringAssert.Contains(json, "\"version\": 1");
    }

    [TestMethod]
    public void PointAttackOnTheChasedEnemy_SharesTheWalk()
    {
        var mortar = _catalog.GetAction("weapon_artillery_mortar");
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(1, 7));
        player.Loadout.Install(new EquipmentInstance("mortar", _catalog.GetEquipment("eq_artillery_mortar")));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(1 + mortar.Range + 8, 7));
        var sim = new CombatSimulation([player, enemy], seed: 1);

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        sim.IssueCommand(new UseActionCommand(player.Id, mortar.Id, TargetPoint: enemy.Position));
        sim.StepBeat();

        Assert.HasCount(2, player.ActionsInMotion);
        Assert.IsTrue(player.ActionsInMotion.All(a => a.Phase == ActionState.Moving));
        Assert.IsFalse(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected));
    }

    [TestMethod]
    public void ZeroRecoveryReactions_StillShareTheBeatsWitBudget()
    {
        var quickPulse = _catalog.GetAction("defense_repulsor") with { RecoveryBeats = 0 };
        var quickBlink = _catalog.GetAction("defense_blink") with { RecoveryBeats = 0 };
        var actions = _catalog.Actions.ToDictionary();
        actions[quickPulse.Id] = quickPulse;
        actions[quickBlink.Id] = quickBlink;
        var catalog = new GameCatalog(actions, _catalog.Equipment, _catalog.StarterConfigurations, _catalog.Enemies,
            _catalog.Rules, _catalog.Materials, _catalog.Encounters, _catalog.Consumables, _catalog.Missions);

        var player = new Combatant("player", "P", Faction.Player,
            CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, catalog, "p"), new Vec2(5, 5), 2);
        player.Loadout.Install(new EquipmentInstance("blink", catalog.GetEquipment("eq_blink_drive")));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1), catalog);

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);
        player.Resources.ReserveWit("pressure", player.Stats.AvailableWit(player.Resources.ReservedWit) - 1);
        sim.IssueCommand(new UseActionCommand(player.Id, quickPulse.Id, enemy.Id));
        sim.IssueCommand(new UseActionCommand(player.Id, quickBlink.Id, TargetPoint: new Vec2(1, 5)));
        sim.StepBeat();

        Assert.AreEqual(1, sim.Events.Count(e => e.ActorId == player.Id && e.Message.Contains("reacts")));
    }

    [TestMethod]
    public void ShapedPartsFromDifferentAssemblies_HaveDistinctIds_AndTransfersNeverLoseParts()
    {
        var a = new Combatant("player_crest", "A", Faction.Player, new CrestLoadout("asm_a", "A"), new Vec2(1, 1));
        var b = new Combatant("player_crest", "B", Faction.Player, new CrestLoadout("asm_b", "B"), new Vec2(1, 1));
        var plan = new ShapingRules.ShapingPlan(0, _catalog.GetEquipment("eq_talon"), null, null);

        ShapingRules.Apply(a, plan, "player_crest_shaped_5", 0, []);
        ShapingRules.Apply(b, plan, "player_crest_shaped_5", 0, []);
        string id = a.Loadout.Installed.Single().InstanceId;
        Assert.AreNotEqual(id, b.Loadout.Installed.Single().InstanceId);

        // A duplicate id at the destination refuses the transfer instead of dropping the part.
        var clash = new CrestLoadout("asm_c", "C", [new EquipmentInstance(id, _catalog.GetEquipment("eq_talon"))]);
        Assert.IsFalse(a.Loadout.TransferComponentTo(id, clash));
        Assert.IsNotNull(a.Loadout.FindById(id));
    }

    [TestMethod]
    public void ReplacedMoveOrder_IsNotReportedAsARejection()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(2, 7));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 7));
        var sim = new CombatSimulation([player, enemy], seed: 1);

        sim.IssueCommand(new MoveCommand(player.Id, new Vec2(22, 13)));
        sim.StepBeat();
        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_vorpal", enemy.Id));
        sim.StepBeat();

        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.OrderReplaced));
        Assert.IsFalse(sim.Events.Any(e => e.Type == CombatEventType.CommandRejected));
    }

    [TestMethod]
    public void NonZeroDefaults_SurviveAJsonRoundTrip_WhenSetToZero()
    {
        var rule = new ReactionRule { ActionId = "defense_blink", MinIncomingDamage = 0 };
        var cfg = ConfigurationCatalog.AgileSkirmish with { ReactionRules = [rule] };

        var json = System.Text.Json.JsonSerializer.Serialize(cfg, ContentSerializer.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<CrestConfiguration>(json, ContentSerializer.Options)!;

        Assert.AreEqual(0, back.ReactionRules[0].MinIncomingDamage);
    }
}
