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

        // Even with a hypothetically low saveTarget of 1, rolling 1 must still fail (always hits)
        var rollOne = service.RollProtection(0, new FixedRandom(1), saveTarget: 1);
        Assert.IsFalse(rollOne.Saved);

        // Even with a high saveTarget of 7, rolling 6 must still save (always saves)
        var rollSix = service.RollProtection(0, new FixedRandom(6), saveTarget: 7);
        Assert.IsTrue(rollSix.Saved);

        // Extra protection dice: [1, 5] -> saved by second die
        var extraDice = service.RollProtection(1, new FixedRandom(1, 5));
        Assert.IsTrue(extraDice.Saved);
        Assert.HasCount(2, extraDice.Rolls);
    }

    [TestMethod]
    public void BlinkDrive_WithinReactionWindow_CompletelyEvadesIncomingAttack()
    {
        var player = CreatePlayer(TemplateCatalog.Strider, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1)); // All rolls = 1 (would always hit)

        // Enemy starts heavy Titan windup (16 ticks)
        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_titan", player.Id));
        for (int i = 0; i < 10; i++) sim.StepTick();

        // Player reacts with Blink Drive before impact
        var blinkResult = sim.IssueCommand(new UseActionCommand(player.Id, "defense_blink", TargetPoint: new Vec2(1, 5)));
        Assert.IsTrue(blinkResult.IsValid);

        for (int i = 0; i < 15; i++) sim.StepTick();

        // No player components should be damaged!
        Assert.IsTrue(player.Loadout.Installed.All(c => c.CurrentDurability == c.Definition.MaxDurability));
        Assert.IsLessThan(4.0, player.Position.X);
    }

    [TestMethod]
    public void AegisSpell_AbsorbsHitsAndReleasesReservedWitWhenDepleted()
    {
        var player = CreatePlayer(TemplateCatalog.Strider, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_skirmisher", new Vec2(8, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1)); // Guaranteed penetration if not blocked

        sim.IssueCommand(new UseActionCommand(player.Id, "spell_aegis"));
        for (int i = 0; i < 8; i++) sim.StepTick();

        Assert.IsTrue(player.HasActiveAegis);
        Assert.AreEqual(2, player.AegisBlocksRemaining);
        Assert.AreEqual(2, player.Resources.ReservedWit);

        // Enemy fires Talon twice -> both absorbed by Aegis
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
        var player = CreatePlayer(TemplateCatalog.Bulwark, new Vec2(5, 5));
        var enemy = CreateEnemy("enemy_artillery", new Vec2(7, 5)); // Siege Mortar has resistance 2
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
        var player = CreatePlayer(TemplateCatalog.Bulwark, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));

        // Disable Thermal Regulator to prove heat consequence
        var reg = player.Loadout.Installed.First(e => e.Definition.Id == "eq_thermal_regulator");
        reg.ApplyDamage(reg.Definition.MaxDurability);

        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));
        var weapon = player.Loadout.Installed.First(e => e.Definition.Id == "eq_dragons_breath");
        int startAmmo = weapon.CurrentAmmo;

        // Fire Dragon's Breath three times rapidly
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
        var player = CreatePlayer(TemplateCatalog.Strider, new Vec2(4, 5), startingMotes: 0);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(8, 5));
        var sim = new CombatSimulation([player, enemy], seed: 1);

        // Gravity Crush requires 1 Mote and Arcane Amplifier
        var noMote = sim.IssueCommand(new UseActionCommand(player.Id, "spell_gravity_crush", enemy.Id));
        Assert.IsFalse(noMote.IsValid);

        player.Resources.AddMotes(2, 5);
        var amp = player.Loadout.Installed.First(e => e.Definition.Id == "eq_spell_amplifier");
        amp.ApplyDamage(amp.Definition.MaxDurability);

        var noAmp = sim.IssueCommand(new UseActionCommand(player.Id, "spell_gravity_crush", enemy.Id));
        Assert.IsFalse(noAmp.IsValid);
    }

    private Combatant CreatePlayer(CrestTemplateDefinition tpl, Vec2 pos, int startingMotes = 2) =>
        new("player", tpl.Name, Faction.Player, CrestLoadout.CreateFromTemplate(tpl, _catalog, "p"), pos, startingMotes);

    private Combatant CreateEnemy(string enemyId, Vec2 pos)
    {
        var def = _catalog.Enemies[enemyId];
        return new Combatant(enemyId, def.Name, Faction.Enemy, CrestLoadout.CreateFromTemplate(def.Template, _catalog, "e"), pos, startingMotes: 2, def.Archetype, def.PreferredRange);
    }
}
