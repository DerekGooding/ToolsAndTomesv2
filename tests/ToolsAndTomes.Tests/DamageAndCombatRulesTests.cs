using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class DamageAndCombatRulesTests
{
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
    public void BlinkDrive_DeclaredDuringIncomingImpact_ResolvesFirstAndEvades()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_titan", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);

        var blinkResult = sim.IssueCommand(new UseActionCommand(player.Id, "defense_blink", TargetPoint: new Vec2(1, 5)));
        Assert.IsTrue(blinkResult.IsValid);

        sim.StepBeat(); // the impact beat: reaction resolves, then the impact

        Assert.IsTrue(IsUndamaged(player));
        Assert.IsLessThan(4.0, player.Position.X);
        Assert.AreEqual(ActionState.Recovery, enemy.State, "Titan Cleave still resolved (and whiffed).");
    }

    [TestMethod]
    public void Repulsor_DeclaredDuringIncomingImpact_CancelsTheAttackBeforeItLands()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_righteous", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "defense_repulsor", enemy.Id)).IsValid);
        sim.StepBeat();

        Assert.IsTrue(IsUndamaged(player));
        Assert.AreEqual(ActionState.Interrupted, enemy.State);
    }

    [TestMethod]
    public void AegisSpell_AbsorbsHitsAndReleasesReservedWitWhenDepleted()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemyA = CreateEnemy("enemy_skirmisher", new Vec2(8, 4), "enemy_a");
        var enemyB = CreateEnemy("enemy_skirmisher", new Vec2(8, 6), "enemy_b");
        var sim = new CombatSimulation([player, enemyA, enemyB], new FixedRandom(1));

        Assert.IsFalse(sim.IssueCommand(new UseActionCommand(player.Id, "spell_aegis")).IsValid,
            "Reactions need an incoming impact.");

        sim.IssueCommand(new UseActionCommand(enemyA.Id, "weapon_talon", player.Id));
        sim.IssueCommand(new UseActionCommand(enemyB.Id, "weapon_talon", player.Id));
        StepUntil(sim, () => enemyA.State == ActionState.Executing);

        Assert.HasCount(2, sim.GetIncomingImpacts(player));
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "spell_aegis")).IsValid);
        sim.StepBeat(); // Aegis resolves first, then both impacts land on it

        Assert.AreEqual(0, player.AegisBlocksRemaining);
        Assert.AreEqual(0, player.Resources.ReservedWit);
        Assert.IsTrue(IsUndamaged(player));
    }

    [TestMethod]
    public void Aegis_AbsorbsDamageButNotEffects()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_titan", player.Id));
        StepUntil(sim, () => enemy.State == ActionState.Executing);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "spell_aegis")).IsValid);
        sim.StepBeat();

        Assert.IsTrue(IsUndamaged(player), "Aegis absorbed the damage.");
        Assert.AreEqual(2.0, player.Position.X, 1e-9, "Titan Cleave's push (2) still applied.");
    }

    [TestMethod]
    public void Reaction_OutsideAnIncomingImpact_IsRejected()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5));
        var enemy = CreateEnemy("enemy_artillery", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_artillery_mortar", TargetPoint: new Vec2(5, 5)));
        sim.StepBeat();
        Assert.AreEqual(ActionState.Windup, enemy.State);

        var tooEarly = sim.IssueCommand(new UseActionCommand(player.Id, "defense_repulsor", enemy.Id));
        Assert.IsFalse(tooEarly.IsValid);
        Assert.Contains("incoming impact", tooEarly.FailureReason!);
    }

    [TestMethod]
    public void Repulsor_DuringAreaImpact_PushesAttackerBack_AndCancelsTheBombardment()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5));
        var enemy = CreateEnemy("enemy_artillery", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(1));

        sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_artillery_mortar", TargetPoint: new Vec2(5, 5)));
        StepUntil(sim, () => enemy.State == ActionState.Executing);

        double startX = enemy.Position.X;
        var repulsor = sim.IssueCommand(new UseActionCommand(player.Id, "defense_repulsor", enemy.Id));
        Assert.IsTrue(repulsor.IsValid);

        sim.StepBeat();
        Assert.IsTrue(IsUndamaged(player), "The bombardment was canceled before it landed.");

        // Push 4 minus displacement resistance 1 (anchor legs) = 3 units
        Assert.AreEqual(startX + 3, enemy.Position.X, 1e-9);
        Assert.AreEqual(ActionState.Interrupted, enemy.State);

        var entry = sim.GetTimelineEntries().Single(e => e.ActorId == enemy.Id);
        var mortar = enemy.Loadout.Installed.First(e => e.Definition.Id == "eq_artillery_mortar");
        Assert.AreEqual(2, enemy.ComponentLockouts[mortar.InstanceId].Total, "The mortar component is locked out.");
        Assert.AreEqual(2, entry.PhaseTotalBeats, "Cancellation penalty = max(2, recovery 2).");
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
    public void Displacement_ResistanceSubtractsWholeUnits_AndNeverReverses()
    {
        Assert.AreEqual(3, DamageResolutionService.EffectiveDisplacement(4, 1));
        Assert.AreEqual(0, DamageResolutionService.EffectiveDisplacement(2, 3));
        Assert.AreEqual(-1, DamageResolutionService.EffectiveDisplacement(-4, 3));
        Assert.AreEqual(0, DamageResolutionService.EffectiveDisplacement(-2, 5));
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
            StepUntil(sim, () => player.State == ActionState.Idle && player.GetCooldown("weapon_dragons_breath") == 0);
            Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_dragons_breath", enemy.Id)).IsValid);
            sim.StepBeat();
        }

        StepUntil(sim, () => player.State == ActionState.Idle);

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

    [TestMethod]
    public void SquareSummon_SpendsFourMotes_AndProducesFastIron()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5), startingMotes: 3);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([player, enemy], seed: 5);

        Assert.IsFalse(sim.IssueCommand(new UseActionCommand(player.Id, "spell_square_summon")).IsValid);

        player.Resources.AddMotes(1, player.Stats.MaxMotes);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "spell_square_summon")).IsValid);

        StepUntil(sim, () => player.Resources.FastIron == 1, maxBeats: 12);
        Assert.IsTrue(sim.Events.Any(e => e.Type == CombatEventType.FastIronProduced));
    }

    [TestMethod]
    public void MoteCostAboveStorageCapacity_IsRejected()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5), startingMotes: 5);
        var book = player.Loadout.Installed.First(e => e.Definition.IsSpellbook);
        var sixMoteSpell = GameCatalog.Default.GetAction("spell_square_summon") with { MoteCost = 6 };

        var result = ActionValidationRules.ValidateUseAction(player, sixMoteSpell, null, null);

        Assert.IsTrue(book.IsOperational);
        Assert.IsFalse(result.IsValid);
        Assert.Contains("can only store", result.FailureReason!);
    }
}
