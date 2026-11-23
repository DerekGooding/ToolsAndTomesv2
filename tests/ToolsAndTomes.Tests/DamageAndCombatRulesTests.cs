using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Tests;

internal sealed class FixedRandom(params int[] rolls) : ISeededRandom
{
    private int _index;

    public int RollD6() => rolls[_index++ % rolls.Length];
    public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
}

[TestClass]
public sealed class DamageAndCombatRulesTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void ProtectionRoll_OneAlwaysHits_SixAlwaysSaves()
    {
        var service = new DamageResolutionService();

        var rollOne = service.RollProtection(0, new FixedRandom(1), saveTarget: 1);
        Assert.IsFalse(rollOne.Saved);

        var rollSix = service.RollProtection(0, new FixedRandom(6), saveTarget: 7);
        Assert.IsTrue(rollSix.Saved);

        var extraDice = service.RollProtection(1, new FixedRandom(1, 5));
        Assert.IsTrue(extraDice.Saved);
        Assert.HasCount(2, extraDice.Rolls);
    }

    [TestMethod]
    public void BlinkDrive_WithinReactionWindow_CompletelyEvadesIncomingAttack()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_titan", player.Id));
        for (int i = 0; i < 10; i++) sim.StepTick();

        var blinkResult = sim.IssueCommand(new UseActionCommand(player.Id, "defense_blink", TargetPoint: new Vec2(1, 5)));
        Assert.IsTrue(blinkResult.IsValid);

        for (int i = 0; i < 15; i++) sim.StepTick();

        Assert.IsTrue(player.Loadout.Installed.All(c => c.CurrentDurability == c.Definition.MaxDurability));
        Assert.IsLessThan(4.0, player.Position.X);
    }

    [TestMethod]
    public void AegisSpell_AbsorbsHitsAndReleasesReservedWitWhenDepleted()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_skirmisher", new Vec2(8, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(player.Id, "spell_aegis"));
        for (int i = 0; i < 8; i++) sim.StepTick();

        Assert.IsTrue(player.HasActiveAegis);
        Assert.AreEqual(2, player.AegisBlocksRemaining);
        Assert.AreEqual(2, player.Resources.ReservedWit);

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_talon", player.Id));
        for (int i = 0; i < 20; i++) sim.StepTick();
        Assert.AreEqual(1, player.AegisBlocksRemaining);

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_talon", player.Id));
        for (int i = 0; i < 20; i++) sim.StepTick();
        Assert.AreEqual(0, player.AegisBlocksRemaining);
        Assert.AreEqual(0, player.Resources.ReservedWit);
        Assert.IsTrue(player.Loadout.Installed.All(c => c.CurrentDurability == c.Definition.MaxDurability));
    }

    [TestMethod]
    public void Repulsor_PushesTargetBack_AndInterruptsCommittedWindup()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5));
        var enemy = CreateEnemy("enemy_artillery", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_artillery_mortar", TargetPoint: new Vec2(5, 5)));
        sim.StepTick();
        Assert.AreEqual(ActionState.Windup, enemy.State);

        double startX = enemy.Position.X;
        var repulsor = sim.IssueCommand(new UseActionCommand(player.Id, "defense_repulsor", enemy.Id));
        Assert.IsTrue(repulsor.IsValid);

        for (int i = 0; i < 6; i++) sim.StepTick();

        Assert.IsGreaterThan(startX, enemy.Position.X);
        Assert.AreEqual(ActionState.Interrupted, enemy.State);
    }

    [TestMethod]
    public void Interruptions_DistinguishDisruptionInterruptionAndCancellation()
    {
        Assert.AreEqual(InterruptionTier.None, InterruptionRules.Evaluate(1, actionResistance: 4, wasDisplaced: false));
        Assert.AreEqual(InterruptionTier.Disruption, InterruptionRules.Evaluate(2, actionResistance: 3, wasDisplaced: false));
        Assert.AreEqual(InterruptionTier.Interruption, InterruptionRules.Evaluate(3, actionResistance: 3, wasDisplaced: false));
        Assert.AreEqual(InterruptionTier.Cancellation, InterruptionRules.Evaluate(5, actionResistance: 3, wasDisplaced: false));
    }

    [TestMethod]
    public void DragonsBreath_ConsumesAmmoAndBuildsHeat_OverheatingWithoutThermalRegulator()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));

        var reg = player.Loadout.Installed.First(e => e.Definition.Id == "eq_thermal_regulator");
        reg.ApplyDamage(reg.Definition.MaxDurability);

        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));
        var weapon = player.Loadout.Installed.First(e => e.Definition.Id == "eq_dragons_breath");
        int startAmmo = weapon.CurrentAmmo;

        for (int shot = 0; shot < 3; shot++)
        {
            sim.IssueCommand(new UseActionCommand(player.Id, "weapon_dragons_breath", enemy.Id));
            for (int t = 0; t < 35; t++) sim.StepTick();
        }

        Assert.AreEqual(startAmmo - 3, weapon.CurrentAmmo);
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.Overheated));
    }

    [TestMethod]
    public void ResourceValidation_BlocksActionsWithoutMotesAmmoOrAmplifier()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5), startingMotes: 0);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(8, 5));
        var sim = new CombatSimulation([player, enemy], seed: 1);

        var noMote = sim.IssueCommand(new UseActionCommand(player.Id, "spell_gravity_crush", enemy.Id));
        Assert.IsFalse(noMote.IsValid);

        player.Resources.AddMotes(2, 5);
        var amp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_spell_amplifier");
        amp.ApplyDamage(amp.Definition.MaxDurability);

        var noAmp = sim.IssueCommand(new UseActionCommand(player.Id, "spell_gravity_crush", enemy.Id));
        Assert.IsFalse(noAmp.IsValid);
    }

    private Combatant CreatePlayer(CrestConfiguration cfg, Vec2 pos, int startingMotes = 2) =>
        new("player", cfg.Name, Faction.Player, CrestLoadout.CreateFromConfiguration(cfg, _catalog, "p"), pos, startingMotes);

    private Combatant CreateEnemy(string enemyId, Vec2 pos)
    {
        var def = _catalog.Enemies[enemyId];
        return new Combatant(enemyId, def.Name, Faction.Enemy, CrestLoadout.CreateFromConfiguration(def.Assembly, _catalog, "e"), pos, startingMotes: 2, def.Archetype, def.PreferredRange);
    }
}
