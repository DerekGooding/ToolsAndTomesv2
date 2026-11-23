using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class ContentTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void ShippedContent_LoadsAndPassesValidation()
    {
        Assert.IsEmpty(ContentValidator.Validate(_catalog));
        Assert.IsTrue(_catalog.Actions.ContainsKey("weapon_acid_spray"));
        Assert.IsTrue(_catalog.Equipment["mob_anchor_legs"].IsLocomotion);
        Assert.HasCount(2, ConfigurationCatalog.HeavyAnchor.ReactionRules);
    }

    [TestMethod]
    public void Content_RoundTripsThroughJsonUnchanged()
    {
        var dir = NewTempDir();
        ContentSerializer.SaveToDirectory(_catalog, dir);
        var reloaded = ContentSerializer.LoadFromDirectory(dir);

        var original = ContentSerializer.Serialize(_catalog);
        var again = ContentSerializer.Serialize(reloaded);
        for (int i = 0; i < original.Count; i++)
            Assert.AreEqual(original[i].Json, again[i].Json, original[i].FileName);
    }

    [TestMethod]
    public void LoadFromDirectory_UsesEditedContent()
    {
        var dir = NewTempDir();
        ContentSerializer.SaveToDirectory(_catalog, dir);
        string path = Path.Combine(dir, ContentSerializer.RulesFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"moteGenerationIntervalBeats\": 5", "\"moteGenerationIntervalBeats\": 3"));

        var modded = ContentSerializer.LoadFromDirectory(dir);

        Assert.AreEqual(3, modded.Rules.MoteGenerationIntervalBeats);
    }

    [TestMethod]
    public void Validator_RejectsContentThatBreaksEngineRules()
    {
        var badReaction = _catalog.GetAction("defense_blink") with { WindupBeats = 2 };
        var noImpact = _catalog.GetAction("weapon_talon") with { ImpactBeats = 0 };
        var orphan = _catalog.GetAction("weapon_talon") with { Id = "weapon_orphan" };
        var brokenGrant = _catalog.Equipment["eq_talon"] with { GrantedActionIds = ["weapon_talon", "weapon_missing"] };
        var badRule = ConfigurationCatalog.AgileSkirmish with
        {
            ReactionRules = [new ReactionRule { ActionId = "weapon_talon" }],
            ComponentIds = ["eq_talon", "mob_stride_legs"]
        };

        var actions = _catalog.Actions.ToDictionary();
        actions[badReaction.Id] = badReaction;
        actions[noImpact.Id] = noImpact;
        actions[orphan.Id] = orphan;
        var equipment = _catalog.Equipment.ToDictionary();
        equipment[brokenGrant.Id] = brokenGrant;
        var configs = _catalog.StarterConfigurations.ToDictionary();
        configs[badRule.Id] = badRule;

        var errors = ContentValidator.Validate(new GameCatalog(actions, equipment, configs, _catalog.Enemies, _catalog.Rules));

        Assert.IsTrue(errors.Any(e => e.Contains("defense_blink") && e.Contains("windupBeats 0")));
        Assert.IsTrue(errors.Any(e => e.Contains("weapon_talon") && e.Contains("impact window")));
        Assert.IsTrue(errors.Any(e => e.Contains("weapon_orphan") && e.Contains("no component grants")));
        Assert.IsTrue(errors.Any(e => e.Contains("unknown action 'weapon_missing'")));
        Assert.IsTrue(errors.Any(e => e.Contains("is not a reaction")));
        Assert.IsTrue(errors.Any(e => e.Contains("has no Core")));
    }

    [TestMethod]
    public void MalformedJson_ReportsTheFile()
    {
        var dir = NewTempDir();
        ContentSerializer.SaveToDirectory(_catalog, dir);
        File.WriteAllText(Path.Combine(dir, ContentSerializer.EquipmentFile), "[ { \"id\": ");

        var ex = Assert.ThrowsExactly<ContentValidationException>(() => ContentSerializer.LoadFromDirectory(dir));
        Assert.Contains("equipment.json", ex.Message);
    }

    private readonly List<string> _tempDirs = [];

    [TestCleanup]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tt_content_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
