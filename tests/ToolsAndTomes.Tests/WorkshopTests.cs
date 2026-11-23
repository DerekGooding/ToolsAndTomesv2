using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class WorkshopTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void Baseline_IsExactlyTheStarterBuildComponents()
    {
        var known = new WorkshopState().KnownComponentIds(_catalog);

        Assert.IsTrue(known.Contains("eq_talon"));
        Assert.IsTrue(known.Contains("eq_shield_generator"), "Shield Generator is in the Heavy Anchor build.");
        foreach (var locked in new[] { "eq_righteous", "eq_falconet", "eq_artillery_mortar", "eq_acid_sprayer", "armor_oathsteel_plating" })
            Assert.IsFalse(known.Contains(locked), locked);
    }

    [TestMethod]
    public void Research_ConsumesSamples_AndUnlocksBlueprints()
    {
        var workshop = new WorkshopState();
        var oathsteel = _catalog.Materials["mat_oathsteel"];

        workshop.AddSamples(oathsteel.Id, 1);
        Assert.IsFalse(workshop.CanResearch(oathsteel));
        Assert.IsFalse(workshop.Research(oathsteel).Succeeded);
        Assert.IsFalse(workshop.CanFabricate(_catalog, "armor_oathsteel_plating"));

        workshop.AddSamples(oathsteel.Id, 2);
        var result = workshop.Research(oathsteel);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEquivalent(new[] { "armor_oathsteel_plating", "eq_righteous" }, result.UnlockedComponentIds.ToList());
        Assert.AreEqual(1, workshop.GetSamples(oathsteel.Id), "Research consumed exactly samplesToResearch.");
        Assert.IsTrue(workshop.CanFabricate(_catalog, "armor_oathsteel_plating"));
        Assert.IsFalse(workshop.Research(oathsteel).Succeeded, "Cannot research twice.");
    }

    [TestMethod]
    public void EncounterRewards_AreDeterministicPerSeed_AndIncludeGuaranteedSamples()
    {
        var encounter = _catalog.Encounters[EncounterFactory.TestEncounterId];
        int expectedTotal = encounter.Rewards.Guaranteed.Sum(g => g.Count) + encounter.Rewards.Draws;

        for (int seed = 1; seed <= 50; seed++)
        {
            var a = RewardRoller.Roll(encounter, seed);
            CollectionAssert.AreEqual(a.ToList(), RewardRoller.Roll(encounter, seed).ToList());
            Assert.AreEqual(expectedTotal, a.Sum(r => r.Count));
            Assert.IsGreaterThanOrEqualTo(1, a.Single(r => r.MaterialId == "mat_oathsteel").Count);
        }

        var everyMaterialSeen = Enumerable.Range(1, 50)
            .SelectMany(seed => RewardRoller.Roll(encounter, seed))
            .Select(r => r.MaterialId)
            .ToHashSet();
        CollectionAssert.IsSubsetOf(encounter.Rewards.Pool.Select(p => p.MaterialId).ToList(), everyMaterialSeen.ToList());
    }

    [TestMethod]
    public void EncounterFromContent_PlacesEnemiesAsDefined()
    {
        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog);
        var sim = EncounterFactory.CreateTestEncounter(loadout, seed: 1);

        Assert.AreEqual(new Vec2(4, 7), sim.FindCombatant(EncounterFactory.PlayerCombatantId)!.Position);
        Assert.AreEqual(2, sim.FindCombatant(EncounterFactory.PlayerCombatantId)!.Resources.CurrentMotes);
        Assert.AreEqual(new Vec2(14.5, 7), sim.FindCombatant("enemy_2_bruiser")!.Position);
        Assert.AreEqual(24, sim.ArenaWidth);
    }

    [TestMethod]
    public void Profile_RoundTrips_AndDropsContentThatNoLongerExists()
    {
        var heavy = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, _catalog, "h");
        var profile = new PlayerProfile
        {
            SelectedAssemblyId = heavy.Id,
            Assemblies =
            [
                heavy.ExportConfiguration(),
                heavy.ExportConfiguration("old") with { ComponentIds = ["eq_talon", "eq_removed_long_ago", "core_aether_dynamo"] }
            ],
            SpareComponentIds = ["eq_blink_drive", "eq_removed_long_ago"],
            Samples = new Dictionary<string, int> { ["mat_corrodite"] = 2, ["mat_gone"] = 4 },
            ResearchedMaterials = ["mat_oathsteel", "mat_gone"],
            EncountersWon = 3
        };

        var loaded = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(profile), _catalog);

        Assert.AreEqual(heavy.Id, loaded.Profile.SelectedAssemblyId);
        CollectionAssert.AreEqual(heavy.ExportConfiguration().ComponentIds.ToList(), loaded.Profile.Assemblies[0].ComponentIds.ToList());
        CollectionAssert.AreEqual(heavy.ReactionRules.ToList(), loaded.Profile.Assemblies[0].ReactionRules.ToList());
        CollectionAssert.AreEqual(new[] { "eq_talon", "core_aether_dynamo" }, loaded.Profile.Assemblies[1].ComponentIds.ToList());
        CollectionAssert.AreEqual(new[] { "eq_blink_drive" }, loaded.Profile.SpareComponentIds.ToList());
        Assert.AreEqual(2, loaded.Profile.Samples["mat_corrodite"]);
        Assert.IsFalse(loaded.Profile.Samples.ContainsKey("mat_gone"));
        CollectionAssert.AreEqual(new[] { "mat_oathsteel" }, loaded.Profile.ResearchedMaterials.ToList());
        Assert.AreEqual(3, loaded.Profile.EncountersWon);
        Assert.HasCount(3, loaded.Warnings);
    }

    [TestMethod]
    public void Validator_RejectsUnobtainableComponents_AndBadRewards()
    {
        var equipment = _catalog.Equipment.ToDictionary();
        equipment["eq_orphan"] = _catalog.Equipment["eq_talon"] with { Id = "eq_orphan", GrantedActionIds = [] };

        var encounters = _catalog.Encounters.ToDictionary();
        var enc = encounters[EncounterFactory.TestEncounterId];
        encounters[enc.Id] = enc with { Rewards = enc.Rewards with { Pool = [new WeightedMaterial("mat_unknown", 1)] } };

        var catalog = new GameCatalog(_catalog.Actions, equipment, _catalog.StarterConfigurations, _catalog.Enemies,
            _catalog.Rules, _catalog.Materials, encounters);
        var errors = ContentValidator.Validate(catalog);

        Assert.IsTrue(errors.Any(e => e.Contains("eq_orphan") && e.Contains("unobtainable")));
        Assert.IsTrue(errors.Any(e => e.Contains("rewards unknown material 'mat_unknown'")));
    }
}
