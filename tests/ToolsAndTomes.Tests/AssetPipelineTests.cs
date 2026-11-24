using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Assets;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class AssetPipelineTests
{
    private static readonly string RepoRoot = AssetProject.FindRepoRoot(AppContext.BaseDirectory);
    private string _temp = "";

    [TestInitialize]
    public void CreateTemp()
    {
        _temp = Path.Combine(Path.GetTempPath(), "tt-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    [TestCleanup]
    public void DeleteTemp()
    {
        if (Directory.Exists(_temp))
            Directory.Delete(_temp, recursive: true);
    }

    /// <summary>The repo's real style, models, and specs, but writing into a temp directory with an empty manifest.</summary>
    private AssetProject TempProject(SpecsFile? specs = null, StyleBible? style = null)
    {
        var real = AssetProject.Load(RepoRoot, catalog: GameCatalog.Default);
        return new AssetProject
        {
            RepoRoot = _temp,
            CandidatesRoot = Path.Combine(_temp, "candidates"),
            Config = real.Config with { OutputRoot = "game" },
            Style = style ?? real.Style,
            Models = real.Models,
            Specs = specs ?? real.Specs,
            Manifest = new AssetManifest(),
            Catalog = GameCatalog.Default
        };
    }

    [TestMethod]
    public void CombatEvents_CarryTheActionThatCausedThem()
    {
        var player = CreatePlayer(ConfigurationCatalog.AgileSkirmish, new Vec2(4, 5));
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(6, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(player.Id, "weapon_talon", enemy.Id)).IsValid);
        StepUntil(sim, () => sim.Events.Any(e => e.Type == CombatEventType.ActionExecuted && e.ActorId == player.Id));

        var started = sim.Events.Single(e => e.Type == CombatEventType.ActionStarted && e.ActorId == player.Id);
        var executed = sim.Events.Single(e => e.Type == CombatEventType.ActionExecuted && e.ActorId == player.Id);
        Assert.AreEqual("weapon_talon", started.ActionId);
        Assert.AreEqual("weapon_talon", executed.ActionId);

        // Hits resolve separately from the action (they're the target's events), so they stay untagged.
        static bool IsHit(CombatEvent e) => e.Type is CombatEventType.ComponentDamaged or CombatEventType.DamageBlocked;
        StepUntil(sim, () => sim.Events.Any(IsHit));
        Assert.IsNull(sim.Events.First(IsHit).ActionId);
    }

    [TestMethod]
    public void ShippedSpecs_ResolveCleanly_AndCoverAllContent()
    {
        var project = AssetProject.Load(RepoRoot, catalog: GameCatalog.Default);
        var plan = AssetPlanner.Plan(project);

        Assert.IsEmpty(plan.Errors, string.Join("\n", plan.Errors));
        Assert.IsEmpty(plan.Warnings, string.Join("\n", plan.Warnings));
        Assert.HasCount(plan.Jobs.Count, plan.Jobs.Select(j => j.AssetId).Distinct().ToList());

        var catalog = GameCatalog.Default;
        int content = catalog.Equipment.Count + catalog.Actions.Count + catalog.Consumables.Count + catalog.Materials.Count;
        Assert.AreEqual(content, plan.Jobs.Count(j => j.Kind == AssetKind.Icon));
        Assert.IsTrue(catalog.Actions.Keys.All(id => plan.Jobs.Any(j => j.AssetId == $"sfx/{id}/impact")));
    }

    [TestMethod]
    public void Specs_ForUnknownContentOrSlots_AreErrors()
    {
        var specs = new SpecsFile
        {
            Icons = [new IconSpec { Source = "eq_does_not_exist", Prompt = "x" }, new IconSpec { Source = "eq_titan", Prompt = "x", Palette = "plaid" }],
            Sfx =
            [
                new SfxSpec { Source = "event:NotAnEvent", Slots = new() { ["play"] = "x" } },
                new SfxSpec { Source = "weapon_titan", Slots = new() { ["recovery"] = "x" } },
                new SfxSpec { Source = "defense_repulsor", Slots = new() { ["windup"] = "x" } } // a reaction: no windup beats
            ]
        };

        var plan = AssetPlanner.Plan(TempProject(specs));

        Assert.HasCount(4, plan.Errors, string.Join("\n", plan.Errors));
        Assert.IsTrue(plan.Warnings.Any(w => w.Contains("defense_repulsor") && w.Contains("no windup")));
    }

    [TestMethod]
    public void UnapprovedBackends_CannotGenerate()
    {
        var project = TempProject();
        Assert.IsTrue(AssetPlanner.Plan(project, imageBackend: "no-such-model").Errors.Any(e => e.Contains("no-such-model")));
        Assert.IsTrue(AssetPlanner.Plan(project, audioBackend: "sdxl").Errors.Any(e => e.Contains("not audio")));
    }

    [TestMethod]
    public void Hash_ChangesWithPromptBackendAndStyle_NotOtherwise()
    {
        var project = TempProject();
        string Hash(AssetProject p, string? backend = null) =>
            AssetPlanner.Plan(p, imageBackend: backend).Jobs.Single(j => j.AssetId == "icon/eq_titan").Hash;

        string baseline = Hash(project);
        Assert.AreEqual(baseline, Hash(TempProject()), "same inputs, same hash");
        Assert.AreNotEqual(baseline, Hash(project, "sdxl"), "switching backend makes placeholders stale");
        Assert.AreNotEqual(baseline, Hash(TempProject(style: project.Style with { Version = project.Style.Version + 1 })));

        var edited = project.Specs with
        {
            Icons = project.Specs.Icons.Select(s => s.Source == "eq_titan" ? s with { Prompt = s.Prompt + ", bigger" } : s).ToList()
        };
        Assert.AreNotEqual(baseline, Hash(TempProject(edited)));
    }

    [TestMethod]
    public void Approve_InstallsChosenVariants_RecordsProvenance_AndRemovesLeftovers()
    {
        var project = TempProject();
        var job = AssetPlanner.Plan(project).Jobs.Single(j => j.AssetId == "sfx/weapon_titan/impact");
        WriteCandidates(project, job, count: 4, failing: 3);

        Assert.AreEqual(AssetState.Missing, AssetPlanner.StateOf(project, job));
        var entry = AssetPipeline.Approve(project, job, [2, 1, 4]);

        CollectionAssert.AreEqual(new[] { "sfx/weapon_titan/impact_1.ogg", "sfx/weapon_titan/impact_2.ogg", "sfx/weapon_titan/impact_3.ogg" }, entry.Files);
        Assert.AreEqual("candidate 2", File.ReadAllText(Path.Combine(project.OutputRoot, entry.Files[0])), "first choice becomes variant 1");
        CollectionAssert.AreEqual(new[] { job.Seed + 1, job.Seed, job.Seed + 3 }, entry.Seeds);
        Assert.IsTrue(entry.Placeholder);
        Assert.AreEqual(AssetState.Approved, AssetPlanner.StateOf(project, job));

        var saved = JsonSerializer.Deserialize<AssetManifest>(File.ReadAllText(project.ManifestPath), AssetJson.Options)!;
        Assert.AreEqual(job.Hash, saved.Assets[job.AssetId].Hash);

        // Re-approving with fewer variants deletes the extra files.
        AssetPipeline.Approve(project, job, [1]);
        Assert.IsTrue(File.Exists(Path.Combine(project.OutputRoot, "sfx/weapon_titan/impact_1.ogg")));
        Assert.IsFalse(File.Exists(Path.Combine(project.OutputRoot, "sfx/weapon_titan/impact_2.ogg")));
    }

    [TestMethod]
    public void Approve_RejectsBadChoices_AndFailingCandidatesUnlessForced()
    {
        var project = TempProject();
        var icon = AssetPlanner.Plan(project).Jobs.Single(j => j.AssetId == "icon/eq_titan");

        Assert.ThrowsExactly<InvalidOperationException>(() => AssetPipeline.Approve(project, icon, [1]), "no candidates yet");

        WriteCandidates(project, icon, count: 3, failing: 2);
        Assert.ThrowsExactly<ArgumentException>(() => AssetPipeline.Approve(project, icon, [1, 3]), "an icon takes exactly one");
        Assert.ThrowsExactly<ArgumentException>(() => AssetPipeline.Approve(project, icon, [9]));
        Assert.ThrowsExactly<InvalidOperationException>(() => AssetPipeline.Approve(project, icon, [2]));
        Assert.AreEqual("icons/eq_titan.png", AssetPipeline.Approve(project, icon, [2], force: true).Files.Single());
    }

    [TestMethod]
    public void Checks_FlagSilenceLoudnessAndMissingSubjects()
    {
        var config = new PipelineConfig();
        var project = TempProject();
        var jobs = AssetPlanner.Plan(project).Jobs;
        var sfx = jobs.First(j => j.Kind == AssetKind.Sfx);
        var icon = jobs.First(j => j.Kind == AssetKind.Icon);

        static Dictionary<string, JsonElement> M(object values) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

        var (warnings, errors) = AssetChecks.Evaluate(config, sfx, M(new { durationSeconds = 1.0, lufs = -16.4, peakDb = -1.0 }));
        Assert.IsEmpty(warnings);
        Assert.IsEmpty(errors);

        (warnings, _) = AssetChecks.Evaluate(config, sfx, M(new { durationSeconds = 1.0, lufs = -24.0, peakDb = -6.0 }));
        Assert.HasCount(1, warnings);

        (_, errors) = AssetChecks.Evaluate(config, sfx, M(new { durationSeconds = 0.1, lufs = (double?)null, peakDb = (double?)null, silent = true }));
        Assert.Contains("silent", errors);

        (_, errors) = AssetChecks.Evaluate(config, icon, M(new { width = 256, coverage = 1.0, subjectFound = false }));
        Assert.HasCount(1, errors);
    }

    /// <summary>Fakes a generator run: candidate files whose content names them, plus the set.json.</summary>
    private static void WriteCandidates(AssetProject project, AssetJob job, int count, int failing)
    {
        string dir = project.CandidateDir(job);
        Directory.CreateDirectory(dir);
        string ext = job.Kind == AssetKind.Icon ? "png" : "ogg";
        var set = new CandidateSet
        {
            AssetId = job.AssetId,
            Hash = job.Hash,
            Backend = job.Backend,
            Model = "test",
            Prompt = job.Prompt,
            Params = [],
            CreatedAt = "2026-11-24T00:00:00Z",
            Candidates = Enumerable.Range(1, count).Select(n =>
            {
                File.WriteAllText(Path.Combine(dir, $"candidate_{n}.{ext}"), $"candidate {n}");
                return new Candidate
                {
                    Index = n, File = $"candidate_{n}.{ext}", Seed = job.Seed + n - 1,
                    Errors = n == failing ? ["broken"] : []
                };
            }).ToList()
        };
        AssetJson.Write(Path.Combine(dir, AssetPipeline.CandidateSetFile), set);
    }
}
