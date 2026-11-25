using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

/// <summary>Tomes and magic (docs/DESIGN.md §3b): core spells, hands and limbs, tome spells, damage types, harness swaps.</summary>
[TestClass]
public sealed class TomeTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    private static CrestConfiguration Build(string name, IReadOnlyList<string> ids, params Holding[] holdings) => new()
    {
        Id = $"cfg_{name}",
        Name = name,
        ComponentIds = ids,
        Holdings = holdings
    };

    private static Combatant Crest(CrestConfiguration cfg, Vec2 pos, Faction faction = Faction.Player, string id = "player", int motes = 3) =>
        new(id, cfg.Name, faction, CrestLoadout.CreateFromConfiguration(cfg, GameCatalog.Default, id), pos, motes);

    private static void Break(EquipmentInstance part) => part.ApplyDamage(part.Definition.MaxDurability);

    // ---- Core spells ------------------------------------------------------------------------------

    [TestMethod]
    public void CoreSpells_NeedNoComponent_AndThePilotCastsOneAtATime()
    {
        var plain = Crest(Build("plain", ["mob_stride_legs", "eq_talon", "core_aether_dynamo"]), new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(8, 5));
        var sim = new CombatSimulation([plain, enemy], seed: 1);

        Assert.IsFalse(plain.Stats.HasTome);
        CollectionAssert.IsSubsetOf(_catalog.Rules.CoreSpellIds.ToList(), plain.Loadout.GetOperationalActionIds().ToList());
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(plain.Id, "spell_push", enemy.Id)).IsValid);
        sim.StepBeat();
        Assert.IsTrue(plain.ActionsInMotion.Any(a => a.SourceEquipmentInstanceId == CrestLoadout.PilotInstanceId));
        Assert.IsNull(plain.FindFreeProvider("spell_pull"), "the pilot is busy with Push");
        Assert.IsFalse(plain.Loadout.Installed.Any(e => e.InstanceId == CrestLoadout.PilotInstanceId), "the pilot is never installed or hit");
    }

    // ---- Hands, limbs, and Wit --------------------------------------------------------------------

    [TestMethod]
    public void TwoHands_ExtraHandheldsAreUnheld_AndOnlyAHeldTomeRaisesWit()
    {
        var crest = CrestLoadout.CreateFromConfiguration(
            Build("crowded", ["eq_talon", "eq_vorpal", "tome_wards", "mob_stride_legs", "core_aether_dynamo"]), _catalog);
        var stats = crest.GetStats();

        Assert.AreEqual(2, stats.Hands);
        Assert.AreEqual(HoldState.OwnHand, crest.HoldStateOf(crest.Installed[0]));
        Assert.AreEqual(HoldState.OwnHand, crest.HoldStateOf(crest.Installed[1]));
        Assert.AreEqual(HoldState.Unheld, crest.HoldStateOf(crest.Installed[2]), "the third handheld has no hand");
        Assert.IsFalse(stats.HasTome);
        Assert.AreEqual(_catalog.Rules.BaseWitWithoutTome, stats.MaxWit);
        CollectionAssert.DoesNotContain(crest.GetOperationalActionIds().ToList(), "spell_aegis");
        Assert.IsTrue(LoadoutRules.GetWarnings(crest).Any(w => w.Contains("No free hand for Tome of Wards")));

        // Reordering the Damage Order decides which handhelds the own hands hold.
        Assert.IsTrue(crest.MoveInDamageOrder(crest.Installed[2].InstanceId, -2));
        Assert.IsTrue(crest.GetStats().HasTome);
        Assert.AreEqual(_catalog.Rules.BaseWitWithTome, crest.GetStats().MaxWit);
    }

    [TestMethod]
    public void AnExtraLimb_HoldsAHandheld_WhichStopsWorkingWhileTheLimbIsBroken()
    {
        // Agile Skirmish: the Vorpal Edge is held by its Articulated Arm.
        var crest = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var vorpal = crest.Installed.First(e => e.Definition.Id == "eq_vorpal");
        var arm = crest.Installed.First(e => e.Definition.Id == "eq_articulated_arm");

        Assert.AreEqual(3, crest.GetStats().Hands);
        Assert.AreEqual(HoldState.Limb, crest.HoldStateOf(vorpal));
        Assert.IsTrue(crest.IsActive(vorpal));
        Assert.IsFalse(LoadoutRules.GetWarnings(crest).Any(w => w.Contains("LIMB BEFORE")), "the arm sits after the Vorpal");

        Break(arm);
        Assert.IsFalse(crest.IsActive(vorpal), "lose the arm, lose what it holds");
        CollectionAssert.DoesNotContain(crest.GetOperationalActionIds().ToList(), "weapon_vorpal");
        arm.Repair();
        Assert.IsTrue(crest.IsActive(vorpal));

        // Put the arm ahead of its item: the Workshop warns loudly.
        while (crest.MoveInDamageOrder(arm.InstanceId, -1)) { }
        Assert.IsTrue(LoadoutRules.GetWarnings(crest).Any(w => w.Contains("LIMB BEFORE ITS ITEM") && w.Contains("Vorpal Edge")));
    }

    [TestMethod]
    public void Holdings_SurviveExportAndImport_AndConfigurationsAreValidated()
    {
        var crest = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var copy = CrestLoadout.CreateFromConfiguration(crest.ExportConfiguration("copy"), _catalog, "c");
        Assert.AreEqual(HoldState.Limb, copy.HoldStateOf(copy.Installed.First(e => e.Definition.Id == "eq_vorpal")));

        var bad = Build("bad", ["eq_talon", "eq_jump_jets", "core_aether_dynamo"], new Holding(0, 1));
        var catalog = new GameCatalog(_catalog.Actions, _catalog.Equipment,
            new Dictionary<string, CrestConfiguration>(_catalog.StarterConfigurations) { [bad.Id] = bad },
            _catalog.Enemies, _catalog.Rules, _catalog.Materials, _catalog.Encounters, _catalog.Consumables, _catalog.Missions, _catalog.Pilots);
        Assert.IsTrue(Core.Content.ContentValidator.Validate(catalog).Any(e => e.Contains("neither a limb nor a harness")));
    }

    // ---- Damage types -----------------------------------------------------------------------------

    [TestMethod]
    public void Electrical_HitsTomeHoldersHarder_AndDisjunction_ScalesWithWitTax()
    {
        var bolt = _catalog.GetAction("spell_lightning_bolt");
        var noTome = Crest(Build("bare", ["eq_talon", "mob_stride_legs", "core_aether_dynamo"]), new Vec2(0, 0));
        var oneTome = Crest(Build("one", ["tome_wards", "mob_stride_legs", "core_aether_dynamo"]), new Vec2(0, 0));
        var twoTomes = Crest(Build("two", ["tome_wards", "tome_storms", "eq_articulated_arm", "tome_force", "mob_stride_legs", "core_aether_dynamo"],
            new Holding(3, 2)), new Vec2(0, 0));

        Assert.AreEqual(1, TraitRules.DamagePerHit(bolt, noTome));
        Assert.AreEqual(2, TraitRules.DamagePerHit(bolt, oneTome));
        Assert.AreEqual(3, twoTomes.Stats.TomesHeld);
        Assert.AreEqual(3, TraitRules.DamagePerHit(bolt, twoTomes), "capped at +2");

        var hexer = bolt with { DamageType = DamageType.Disjunction };
        Assert.AreEqual(1 + twoTomes.Stats.EquipmentWitTax / 3, TraitRules.DamagePerHit(hexer, twoTomes));
        Assert.AreEqual(1, TraitRules.DamagePerHit(hexer, noTome), "Talon's 1 Wit tax is under 3");
    }

    // ---- Tome spells ------------------------------------------------------------------------------

    [TestMethod]
    public void WardAlly_GivesAnAllyAnAegis_WithoutTakingItsWit_AndCantTargetEnemies()
    {
        var caster = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(4, 5), startingMotes: 3);
        var ally = Crest(Build("ally", ["eq_talon", "mob_stride_legs", "core_aether_dynamo"]), new Vec2(6, 5), id: "ally_x");
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(12, 5));
        var sim = new CombatSimulation([caster, ally, enemy], seed: 3);

        Assert.IsFalse(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_ward_ally", enemy.Id)).IsValid);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_ward_ally", ally.Id)).IsValid);
        StepUntil(sim, () => ally.HasActiveAegis, maxBeats: 6);
        Assert.AreEqual(0, ally.Resources.ReservedWit);
        Assert.AreEqual(1, ally.AegisBlocksRemaining);
    }

    [TestMethod]
    public void Bulwark_AddsAProtectionDie_ToTheLanceNearby()
    {
        var caster = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(4, 5), startingMotes: 4);
        var near = Crest(Build("near", ["eq_talon", "mob_stride_legs", "core_aether_dynamo"]), new Vec2(5, 6), id: "ally_near");
        var far = Crest(Build("far", ["eq_talon", "mob_stride_legs", "core_aether_dynamo"]), new Vec2(12, 12), id: "ally_far");
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([caster, near, far, enemy], seed: 3);

        int before = near.BonusProtectionDice;
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_bulwark")).IsValid);
        StepUntil(sim, () => near.ProtectionBuffBeats > 0, maxBeats: 6);
        Assert.AreEqual(before + 1, near.BonusProtectionDice);
        Assert.AreEqual(0, far.ProtectionBuffBeats, "out of range");
        Assert.IsGreaterThan(0, caster.ProtectionBuffBeats, "the caster is covered too");
    }

    [TestMethod]
    public void LeadenAir_Slows_AndDelaysTheNextWindup_AnchorBind_Pins()
    {
        var force = ConfigurationCatalog.AgileSkirmish with
        {
            ComponentIds = ConfigurationCatalog.AgileSkirmish.ComponentIds.Select(id => id == "tome_wards" ? "tome_force" : id).ToList()
        };
        var caster = CreatePlayer(force, new Vec2(4, 5), startingMotes: 3);
        var enemy = CreateEnemy("enemy_skirmisher", new Vec2(9, 5));
        var sim = new CombatSimulation([caster, enemy], seed: 3);
        int speed = enemy.Stats.MoveSpeed;

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_leaden_air", enemy.Id)).IsValid);
        StepUntil(sim, () => enemy.SlowBeatsRemaining > 0, maxBeats: 6);
        Assert.AreEqual(Math.Max(1, speed - 2), enemy.EffectiveMoveSpeed);
        Assert.AreEqual(1, enemy.PendingWindupDelay);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(enemy.Id, "weapon_talon", caster.Id)).IsValid);
        StepUntil(sim, () => enemy.ActionsInMotion.Any(a => a.Phase == ActionState.Windup), maxBeats: 4);
        var talon = enemy.ActionsInMotion.First(a => a.Definition.Id == "weapon_talon");
        Assert.AreEqual(_catalog.GetAction("weapon_talon").WindupBeats + 1, talon.PhaseTotalBeats);
        Assert.AreEqual(0, enemy.PendingWindupDelay, "the delay is used up");

        StepUntil(sim, () => caster.GetCooldown("spell_leaden_air") == 0 && caster.State == ActionState.Idle, maxBeats: 12);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_anchor_bind", enemy.Id)).IsValid);
        StepUntil(sim, () => enemy.IsPinned, maxBeats: 8);
        var pinnedAt = enemy.Position;
        Assert.AreEqual(0, enemy.EffectiveMoveSpeed);
        sim.IssueCommand(new MoveCommand(enemy.Id, new Vec2(20, 5)));
        sim.StepBeat();
        Assert.AreEqual(pinnedAt, enemy.Position, "pinned Crests don't move");
    }

    [TestMethod]
    public void ChainLightning_JumpsToNearbyEnemies()
    {
        var storms = ConfigurationCatalog.AgileSkirmish with
        {
            ComponentIds = ConfigurationCatalog.AgileSkirmish.ComponentIds.Select(id => id == "tome_wards" ? "tome_storms" : id).ToList()
        };
        var caster = CreatePlayer(storms, new Vec2(4, 5), startingMotes: 3);
        var a = CreateEnemy("enemy_bruiser", new Vec2(9, 5), "enemy_a");
        var b = CreateEnemy("enemy_bruiser", new Vec2(11, 5), "enemy_b");
        var c = CreateEnemy("enemy_bruiser", new Vec2(13, 5), "enemy_c");
        var far = CreateEnemy("enemy_bruiser", new Vec2(22, 13), "enemy_far");
        var sim = new CombatSimulation([caster, a, b, c, far], new FixedRandom(1)); // every save fails

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_chain_lightning", a.Id)).IsValid);
        StepUntil(sim, () => sim.Events.Any(e => e.ActionId == "spell_chain_lightning" && e.Type == CombatEventType.ActionExecuted), maxBeats: 8);
        sim.StepBeat();
        foreach (var hit in new[] { a, b, c })
            Assert.IsFalse(IsUndamaged(hit), hit.Id);
        Assert.IsTrue(IsUndamaged(far), "out of chain range");
    }

    [TestMethod]
    public void QuickMend_SpendsFastIron_AndConjuredBlade_LastsUntilTheFightEnds()
    {
        var codex = ConfigurationCatalog.AgileSkirmish with
        {
            ComponentIds = ConfigurationCatalog.AgileSkirmish.ComponentIds.Select(id => id == "tome_wards" ? "tome_iron_codex" : id).ToList()
        };
        var caster = CreatePlayer(codex, new Vec2(4, 5), startingMotes: 3);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([caster, enemy], seed: 3);
        var jets = caster.Loadout.Installed.First(e => e.Definition.Id == "eq_jump_jets");
        Break(jets);
        int iron = caster.TotalFastIron;

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_quick_mend", caster.Id)).IsValid);
        StepUntil(sim, () => jets.IsOperational, maxBeats: 6);
        Assert.AreEqual(iron - 1, caster.TotalFastIron);

        // Conjured Blade needs a free hand: drop the Talon (it lets go when broken) to free one.
        Break(caster.Loadout.Installed.First(e => e.Definition.Id == "eq_talon"));
        Assert.IsTrue(caster.Loadout.HasFreeHand());
        StepUntil(sim, () => caster.State == ActionState.Idle, maxBeats: 6);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_conjured_blade")).IsValid);
        StepUntil(sim, () => caster.Loadout.Installed.Any(e => e.IsConjured), maxBeats: 6);
        var blade = caster.Loadout.Installed.First(e => e.IsConjured);
        Assert.IsTrue(caster.Loadout.IsActive(blade));
        CollectionAssert.Contains(caster.Loadout.GetOperationalActionIds().ToList(), "weapon_iron_blade");
        CollectionAssert.DoesNotContain(caster.Loadout.ExportConfiguration().ComponentIds.ToList(), "eq_iron_blade", "never saved");

        caster.PrepareForEncounter(new Vec2(4, 5));
        Assert.IsFalse(caster.Loadout.Installed.Any(e => e.IsConjured), "gone after the fight");
    }

    [TestMethod]
    public void AHarness_SwapsItsStowedItemIntoAHand()
    {
        var cfg = Build("swapper", ["eq_talon", "tome_wards", "eq_righteous", "eq_weapon_harness", "mob_stride_legs", "core_aether_dynamo"],
            new Holding(2, 3));
        var crest = Crest(cfg, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(20, 5));
        var sim = new CombatSimulation([crest, enemy], seed: 3);
        var talon = crest.Loadout.Installed[0];
        var blade = crest.Loadout.Installed[2];

        Assert.AreEqual(HoldState.Stowed, crest.Loadout.HoldStateOf(blade));
        Assert.IsFalse(crest.Loadout.IsActive(blade));
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(crest.Id, "harness_swap", OwnComponentInstanceId: talon.InstanceId)).IsValid);
        StepUntil(sim, () => crest.Loadout.HoldStateOf(blade) == HoldState.OwnHand, maxBeats: 6);
        Assert.AreEqual(HoldState.Stowed, crest.Loadout.HoldStateOf(talon));
        CollectionAssert.Contains(crest.Loadout.GetOperationalActionIds().ToList(), "weapon_righteous");
        CollectionAssert.DoesNotContain(crest.Loadout.GetOperationalActionIds().ToList(), "weapon_talon");
    }

    // ---- Saves ------------------------------------------------------------------------------------

    [TestMethod]
    public void OldProfiles_TurnTheSpellbookIntoATomeOfWards()
    {
        var old = new PlayerProfile
        {
            Assemblies = [ConfigurationCatalog.HeavyAnchor with { ComponentIds = ["eq_titan", "spellbook_grimoire", "mob_anchor_legs", "core_aether_furnace"] }],
            SpareComponentIds = ["spellbook_grimoire"]
        };
        var loaded = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(old), _catalog);
        CollectionAssert.Contains(loaded.Profile.Assemblies[0].ComponentIds.ToList(), "tome_wards");
        CollectionAssert.Contains(loaded.Profile.SpareComponentIds.ToList(), "tome_wards");
        Assert.IsTrue(loaded.Warnings.Any(w => w.Contains("Tome of Wards")));
    }
}
