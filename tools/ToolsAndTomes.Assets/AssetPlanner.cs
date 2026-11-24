using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Assets;

public enum AssetKind
{
    Icon,
    Sfx
}

public enum AssetState
{
    /// <summary>Never approved.</summary>
    Missing,

    /// <summary>Approved under a different spec, style, or backend (or its files are gone): regenerate.</summary>
    Stale,

    /// <summary>Approved and current.</summary>
    Approved
}

/// <summary>One asset to produce: a fully resolved spec (prompt, palette, seed, parameters) and where it lands.</summary>
public sealed record AssetJob
{
    public required string AssetId { get; init; }
    public required AssetKind Kind { get; init; }
    public required string Source { get; init; }
    public required string Slot { get; init; }
    public required string Prompt { get; init; }
    public required string NegativePrompt { get; init; }
    public required IReadOnlyList<string> Palette { get; init; }
    public required int Seed { get; init; }
    public required SortedDictionary<string, object> Params { get; init; }
    public required string Backend { get; init; }
    public required int Candidates { get; init; }

    /// <summary>Most files one approval can produce (1 for icons; sound variants for SFX).</summary>
    public required int Variants { get; init; }

    /// <summary>Output paths relative to the output root, one per variant.</summary>
    public required IReadOnlyList<string> OutputFiles { get; init; }

    public required string Hash { get; init; }
}

/// <summary>Everything under assets/ plus the game content, loaded together.</summary>
public sealed class AssetProject
{
    public required string RepoRoot { get; init; }
    public required string CandidatesRoot { get; init; }
    public required PipelineConfig Config { get; init; }
    public required StyleBible Style { get; init; }
    public required ModelRegistry Models { get; init; }
    public required SpecsFile Specs { get; init; }
    public required AssetManifest Manifest { get; init; }
    public required GameCatalog Catalog { get; init; }

    public string AssetsDir => Path.Combine(RepoRoot, "assets");
    public string ManifestPath => Path.Combine(AssetsDir, "manifest.json");
    public string OutputRoot => Path.GetFullPath(Path.Combine(RepoRoot, Config.OutputRoot));

    public static string DefaultCandidatesRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsAndTomes", "asset-candidates");

    public static AssetProject Load(string repoRoot, string? candidatesRoot = null, GameCatalog? catalog = null)
    {
        string assets = Path.Combine(repoRoot, "assets");
        return new AssetProject
        {
            RepoRoot = repoRoot,
            CandidatesRoot = candidatesRoot ?? DefaultCandidatesRoot,
            Config = AssetJson.Read<PipelineConfig>(Path.Combine(assets, "pipeline.json")),
            Style = AssetJson.Read<StyleBible>(Path.Combine(assets, "style.json")),
            Models = AssetJson.Read<ModelRegistry>(Path.Combine(assets, "models.json")),
            Specs = AssetJson.Read<SpecsFile>(Path.Combine(assets, "specs.json")),
            Manifest = AssetJson.Read<AssetManifest>(Path.Combine(assets, "manifest.json")),
            Catalog = catalog ?? ContentSerializer.LoadEmbedded()
        };
    }

    /// <summary>Walks up from <paramref name="start"/> to the directory holding ToolsAndTomes.slnx.</summary>
    public static string FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ToolsAndTomes.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException($"No ToolsAndTomes.slnx above '{start}'. Pass --repo <path>.");
    }

    public ModelEntry? FindModel(string backend) => Models.Models.FirstOrDefault(m => m.Backend == backend);

    public string CandidateDir(AssetJob job) =>
        Path.Combine(CandidatesRoot, Path.Combine(job.AssetId.Split('/')), job.Hash);
}

public sealed record AssetPlan(IReadOnlyList<AssetJob> Jobs, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>Resolves specs against the style bible and the game content, and checks them.</summary>
public static class AssetPlanner
{
    public static readonly string[] ActionSlots = ["windup", "impact"];
    public const string EventSlot = "play";
    public const string EventPrefix = "event:";

    public static AssetPlan Plan(AssetProject project, string? imageBackend = null, string? audioBackend = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var jobs = new List<AssetJob>();
        var catalog = project.Catalog;
        var config = project.Config;
        imageBackend ??= config.ImageBackend;
        audioBackend ??= config.AudioBackend;

        CheckBackend(project, imageBackend, "image", errors);
        CheckBackend(project, audioBackend, "audio", errors);

        foreach (var spec in project.Specs.Icons)
        {
            string? category = IconCategory(catalog, spec.Source);
            if (category is null)
            {
                errors.Add($"icon '{spec.Source}': no equipment, action, consumable, or material has this id.");
                continue;
            }

            string paletteName = spec.Palette ?? project.Style.CategoryPalettes.GetValueOrDefault(category) ?? "";
            if (!project.Style.Palettes.TryGetValue(paletteName, out var palette))
            {
                errors.Add($"icon '{spec.Source}': unknown palette '{paletteName}' (see assets/style.json).");
                continue;
            }

            string assetId = $"icon/{spec.Source}";
            var parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["size"] = config.Icon.Size,
                ["steps"] = config.Icon.Steps,
                ["guidance"] = config.Icon.Guidance,
                ["background"] = project.Style.Icon.Background ?? "#D0D0D0"
            };
            if (config.Icon.RenderSize is { } render)
                parameters["renderSize"] = render;

            jobs.Add(Finish(project, new AssetJob
            {
                AssetId = assetId,
                Kind = AssetKind.Icon,
                Source = spec.Source,
                Slot = "icon",
                Prompt = Fill(project.Style.Icon.Prompt, spec.Prompt, palette.Words),
                NegativePrompt = project.Style.Icon.NegativePrompt,
                Palette = palette.Colors,
                Seed = spec.Seed ?? DefaultSeed(assetId),
                Params = parameters,
                Backend = imageBackend,
                Candidates = config.Icon.Candidates,
                Variants = 1,
                OutputFiles = [$"icons/{spec.Source}.png"],
                Hash = ""
            }));
        }

        foreach (var spec in project.Specs.Sfx)
        {
            bool isEvent = spec.Source.StartsWith(EventPrefix, StringComparison.Ordinal);
            string[] allowedSlots;
            if (isEvent)
            {
                if (!Enum.TryParse<CombatEventType>(spec.Source[EventPrefix.Length..], out _))
                {
                    errors.Add($"sfx '{spec.Source}': unknown combat event type.");
                    continue;
                }
                allowedSlots = [EventSlot];
            }
            else if (catalog.Actions.TryGetValue(spec.Source, out var action))
            {
                allowedSlots = ActionSlots;
                if (spec.Slots.ContainsKey("windup") && action.WindupBeats == 0)
                    warnings.Add($"sfx '{spec.Source}': has a windup sound but the action has no windup beats (it never plays).");
            }
            else
            {
                errors.Add($"sfx '{spec.Source}': no action has this id (events use '{EventPrefix}<CombatEventType>').");
                continue;
            }

            if (spec.Slots.Count == 0)
                errors.Add($"sfx '{spec.Source}': no slots.");

            foreach (var (slot, subject) in spec.Slots)
            {
                if (!allowedSlots.Contains(slot))
                {
                    errors.Add($"sfx '{spec.Source}': slot '{slot}' is not one of {string.Join(", ", allowedSlots)}.");
                    continue;
                }

                string key = SoundKey(spec.Source);
                string assetId = $"sfx/{key}/{slot}";
                var parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["durationSeconds"] = spec.DurationSeconds ?? config.Sfx.DurationSeconds,
                    ["steps"] = config.Sfx.Steps,
                    ["guidance"] = config.Sfx.Guidance,
                    ["targetLufs"] = config.Sfx.TargetLufs,
                    ["maxPeakDb"] = config.Sfx.MaxPeakDb
                };

                jobs.Add(Finish(project, new AssetJob
                {
                    AssetId = assetId,
                    Kind = AssetKind.Sfx,
                    Source = spec.Source,
                    Slot = slot,
                    Prompt = Fill(project.Style.Sfx.Prompt, subject, ""),
                    NegativePrompt = project.Style.Sfx.NegativePrompt,
                    Palette = [],
                    Seed = spec.Seed ?? DefaultSeed(assetId),
                    Params = parameters,
                    Backend = audioBackend,
                    Candidates = Math.Max(config.Sfx.Candidates, config.Sfx.Variants),
                    Variants = config.Sfx.Variants,
                    OutputFiles = Enumerable.Range(1, config.Sfx.Variants).Select(n => $"sfx/{key}/{slot}_{n}.ogg").ToList(),
                    Hash = ""
                }));
            }
        }

        foreach (var duplicate in jobs.GroupBy(j => j.AssetId).Where(g => g.Count() > 1))
            errors.Add($"'{duplicate.Key}' is specified {duplicate.Count()} times.");

        // Coverage: shipped content without art or sound.
        var iconSources = project.Specs.Icons.Select(s => s.Source).ToHashSet();
        var missingIcons = catalog.Equipment.Keys.Concat(catalog.Actions.Keys).Concat(catalog.Consumables.Keys).Concat(catalog.Materials.Keys)
            .Where(id => !iconSources.Contains(id)).ToList();
        if (missingIcons.Count > 0)
            warnings.Add($"no icon spec for: {string.Join(", ", missingIcons)}");

        var soundedActions = project.Specs.Sfx.Where(s => s.Slots.ContainsKey("impact")).Select(s => s.Source).ToHashSet();
        var silentActions = catalog.Actions.Keys.Where(id => !soundedActions.Contains(id)).ToList();
        if (silentActions.Count > 0)
            warnings.Add($"no impact sound for actions: {string.Join(", ", silentActions)}");

        return new AssetPlan(jobs, errors, warnings);
    }

    public static AssetState StateOf(AssetProject project, AssetJob job)
    {
        if (!project.Manifest.Assets.TryGetValue(job.AssetId, out var entry))
            return AssetState.Missing;
        if (entry.Hash != job.Hash)
            return AssetState.Stale;
        return entry.Files.All(f => File.Exists(Path.Combine(project.OutputRoot, f))) ? AssetState.Approved : AssetState.Stale;
    }

    /// <summary>Folder name for a sound source: the action id, or "event_&lt;Type&gt;" for engine events.</summary>
    public static string SoundKey(string source) =>
        source.StartsWith(EventPrefix, StringComparison.Ordinal) ? "event_" + source[EventPrefix.Length..] : source;

    /// <summary>A stable seed derived from the asset id (FNV-1a), so specs don't need hand-picked seeds.</summary>
    public static int DefaultSeed(string assetId)
    {
        uint hash = 2166136261;
        foreach (char c in assetId)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return (int)(hash % 1_000_000_000);
    }

    private static AssetJob Finish(AssetProject project, AssetJob job) => job with { Hash = ComputeHash(project, job) };

    /// <summary>
    /// Everything that changes the output: kind, prompts, palette, seed, parameters, backend, and the style version.
    /// </summary>
    public static string ComputeHash(AssetProject project, AssetJob job)
    {
        var model = project.FindModel(job.Backend);
        string canonical = JsonSerializer.Serialize(new
        {
            kind = job.Kind.ToString(),
            prompt = job.Prompt,
            negative = job.NegativePrompt,
            palette = job.Palette,
            seed = job.Seed,
            variants = job.Variants,
            parameters = job.Params,
            backend = job.Backend,
            style = project.Style.Version,
            source = model?.Source
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    private static string? IconCategory(GameCatalog catalog, string id)
    {
        if (catalog.Equipment.TryGetValue(id, out var equipment))
            return equipment.Category.ToString();
        if (catalog.Actions.TryGetValue(id, out var action))
            return action.Category == ActionCategory.Spell ? "Spell" : action.Category.ToString();
        if (catalog.Consumables.ContainsKey(id))
            return "Consumable";
        if (catalog.Materials.ContainsKey(id))
            return "Material";
        return null;
    }

    private static void CheckBackend(AssetProject project, string backend, string kind, List<string> errors)
    {
        var model = project.FindModel(backend);
        if (model is null)
            errors.Add($"{kind} backend '{backend}' is not in assets/models.json.");
        else if (model.Kind != kind)
            errors.Add($"backend '{backend}' is a {model.Kind} model, not {kind}.");
        else if (!model.Usable)
            errors.Add($"backend '{backend}' has license status '{model.Status}': only approved or capped models may generate.");
    }

    private static string Fill(string template, string subject, string paletteWords) =>
        template.Replace("{subject}", subject).Replace("{palette}", paletteWords);
}
