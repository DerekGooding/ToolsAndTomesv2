using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class ReactionRuleTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void MeleeRule_AutomaticallyRepulsesAnIncomingMeleeImpact()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);
        sim.StepBeat(); // no manual input: the Workshop rule reacts

        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.ReactionRuleTriggered && e.Message.Contains("Repulsor")));
        Assert.IsTrue(IsUndamaged(player));
        Assert.AreEqual(ActionState.Interrupted, enemy.State);
    }

    [TestMethod]
    public void DamageThreshold_IgnoresLightImpacts_AndTriggersOnHeavyOnes()
    {
        // Agile starter rule: Blink when an incoming impact would deal >= 2 damage.
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(6, 7));
        var skirmisher = CreateEnemy("enemy_skirmisher", new Vec2(10, 7), "enemy_a");
        var artillery = CreateEnemy("enemy_artillery", new Vec2(16, 7), "enemy_b");
        var sim = new CombatSimulation([player, skirmisher, artillery], new FixedRandom(6));

        sim.IssueCommand(new UseActionCommand(skirmisher.Id, "weapon_talon", player.Id)); // 1 damage
        StepUntil(sim, () => skirmisher.State == ActionState.Executing);
        sim.StepBeat();
        Assert.IsFalse(sim.Events.Any(e => e.Type == CombatEventType.CombatantBlinked), "1-damage shot is below the threshold.");

        var start = player.Position;
        sim.IssueCommand(new UseActionCommand(artillery.Id, "weapon_artillery_mortar", TargetPoint: player.Position)); // 2 damage area
        StepUntil(sim, () => artillery.State == ActionState.Executing);
        sim.StepBeat();

        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.CombatantBlinked));
        Assert.IsGreaterThan(3.0, start.DistanceTo(player.Position), "Blinked out of the blast.");
        Assert.IsLessThan(start.X, player.Position.X, "Blinked away from the nearest enemy.");
    }

    [TestMethod]
    public void ManualReaction_TakesPrecedenceOverRules()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5), startingMotes: 2);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "spell_aegis")).IsValid);
        sim.StepBeat();

        Assert.IsFalse(sim.Events.Any(e => e.Type == CombatEventType.ReactionRuleTriggered));
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.AegisActivated));
    }

    [TestMethod]
    public void ReactionRules_SurviveExportAndRestore_AndCanBeReordered()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog, "p");
        Assert.HasCount(2, loadout.ReactionRules);

        Assert.IsTrue(loadout.MoveReactionRule(1, -1));
        Assert.AreEqual("spell_aegis", loadout.ReactionRules[0].ActionId);

        var restored = CrestLoadout.CreateFromConfiguration(loadout.ExportConfiguration("copy"), _catalog, "r");
        CollectionAssert.AreEqual(loadout.ReactionRules.ToList(), restored.ReactionRules.ToList());

        Assert.IsTrue(restored.RemoveReactionRuleAt(0));
        Assert.HasCount(1, restored.ReactionRules);
    }
}
