using System.Text.Json;
using ToolsAndTomes.Assets;

// Asset pipeline orchestrator (docs/ASSET_PIPELINES.md). Generator services live in tools/asset-services.
//
//   dotnet run --project tools/ToolsAndTomes.Assets -- <command> [options]
//
//   status                      what is approved, stale, missing, or waiting for review
//   validate                    check specs, palettes, backends, and the manifest; list content without assets
//   health                      ask the generator services which backends are ready
//   generate                    make candidates for every missing or stale asset, then write the review page
//   review                      rewrite the review page (candidates side by side, approve commands)
//   approve <assetId> <n[,n…]>  install candidate(s) into the game and record them in assets/manifest.json
//   reject <assetId>            discard the candidates waiting for an asset
//
// Options: --filter <text> (asset ids containing it)  --force (regenerate even if approved or waiting)
//          --limit <n>  --auto-approve (approve the first passing candidates: for placeholders)
//          --image-backend <b>  --audio-backend <b>  --image-url <u>  --audio-url <u>
//          --by <name>  --note <text>  --repo <dir>  --candidates <dir>

string? command = null, filter = null, imageBackend = null, audioBackend = null, imageUrl = null, audioUrl = null;
string? repo = null, candidatesRoot = null, approvedBy = null, note = null;
bool force = false, autoApprove = false;
int limit = int.MaxValue;
var positional = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
    switch (arg)
    {
        case "--filter": filter = Next(); break;
        case "--force": force = true; break;
        case "--limit": limit = int.Parse(Next()); break;
        case "--auto-approve": autoApprove = true; break;
        case "--image-backend": imageBackend = Next(); break;
        case "--audio-backend": audioBackend = Next(); break;
        case "--image-url": imageUrl = Next(); break;
        case "--audio-url": audioUrl = Next(); break;
        case "--by": approvedBy = Next(); break;
        case "--note": note = Next(); break;
        case "--repo": repo = Next(); break;
        case "--candidates": candidatesRoot = Next(); break;
        default:
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown option '{arg}'.");
                return 2;
            }
            if (command is null) command = arg;
            else positional.Add(arg);
            break;
    }
}

if (command is null)
{
    Console.WriteLine("Commands: status, validate, health, generate, review, approve <assetId> <n[,n…]>, reject <assetId>");
    return 2;
}

AssetProject project;
try
{
    var loaded = AssetProject.Load(repo ?? AssetProject.FindRepoRoot(Directory.GetCurrentDirectory()), candidatesRoot);
    var config = loaded.Config with
    {
        ImageService = imageUrl ?? loaded.Config.ImageService,
        AudioService = audioUrl ?? loaded.Config.AudioService
    };
    project = new AssetProject
    {
        RepoRoot = loaded.RepoRoot, CandidatesRoot = loaded.CandidatesRoot, Config = config, Style = loaded.Style,
        Models = loaded.Models, Specs = loaded.Specs, Manifest = loaded.Manifest, Catalog = loaded.Catalog
    };
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var plan = AssetPlanner.Plan(project, imageBackend, audioBackend);
var jobs = plan.Jobs.Where(j => filter is null || j.AssetId.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

AssetJob? FindJob(string id)
{
    var job = plan.Jobs.FirstOrDefault(j => j.AssetId == id);
    if (job is null)
        Console.Error.WriteLine($"No asset '{id}'. Asset ids look like icon/eq_titan or sfx/weapon_titan/impact (see `status`).");
    return job;
}

bool ReportPlanErrors()
{
    foreach (string e in plan.Errors) Console.Error.WriteLine($"error: {e}");
    return plan.Errors.Count > 0;
}

switch (command)
{
    case "status":
    {
        ReportPlanErrors();
        var rows = jobs.Select(j => (Job: j, State: AssetPlanner.StateOf(project, j), Waiting: AssetPipeline.LoadCandidates(project, j) is not null)).ToList();
        Console.WriteLine($"{rows.Count} assets: {rows.Count(r => r.State == AssetState.Approved)} approved, " +
                          $"{rows.Count(r => r.State == AssetState.Stale)} stale, {rows.Count(r => r.State == AssetState.Missing)} missing, " +
                          $"{rows.Count(r => r.State != AssetState.Approved && r.Waiting)} waiting for review.");
        int placeholders = rows.Count(r => r.State == AssetState.Approved && project.Manifest.Assets[r.Job.AssetId].Placeholder);
        if (placeholders > 0)
            Console.WriteLine($"{placeholders} approved asset(s) are procedural placeholders.");
        var open = rows.Where(r => r.State != AssetState.Approved).ToList();
        foreach (var (job, state, waiting) in open.Take(limit == int.MaxValue ? 25 : limit))
            Console.WriteLine($"  {state,-8} {job.AssetId}{(waiting ? "  (candidates waiting)" : "")}");
        if (open.Count > (limit == int.MaxValue ? 25 : limit))
            Console.WriteLine($"  … and {open.Count - (limit == int.MaxValue ? 25 : limit)} more (--limit n, or --filter)");
        return plan.Errors.Count > 0 ? 1 : 0;
    }

    case "validate":
    {
        bool failed = ReportPlanErrors();
        foreach (string w in plan.Warnings) Console.WriteLine($"warning: {w}");
        foreach (var (id, entry) in project.Manifest.Assets)
        {
            if (plan.Jobs.All(j => j.AssetId != id))
                Console.WriteLine($"warning: manifest has '{id}' but no spec produces it (orphaned).");
            foreach (string file in entry.Files.Where(f => !File.Exists(Path.Combine(project.OutputRoot, f))))
            {
                Console.Error.WriteLine($"error: {id}: approved file missing: {file}");
                failed = true;
            }
            if (project.FindModel(entry.Backend) is { Usable: false } model)
            {
                Console.Error.WriteLine($"error: {id}: made with '{entry.Backend}', whose license status is now '{model.Status}'.");
                failed = true;
            }
        }
        var capped = project.Manifest.Assets.Values.Where(e => project.FindModel(e.Backend)?.Status == "capped").GroupBy(e => e.Backend).ToList();
        foreach (var group in capped)
            Console.WriteLine($"note: {group.Count()} approved asset(s) use '{group.Key}' (revenue-capped license; see assets/models.json).");
        Console.WriteLine(failed ? "Validation failed." : $"OK: {plan.Jobs.Count} asset specs.");
        return failed ? 1 : 0;
    }

    case "health":
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new GeneratorClient(http);
        int unreachable = 0;
        foreach (var (kind, url) in new[] { ("image", project.Config.ImageService), ("audio", project.Config.AudioService) })
        {
            try
            {
                var health = await client.HealthAsync(url);
                Console.WriteLine($"{kind} service {url}:");
                foreach (var backend in health.GetProperty("backends").EnumerateObject())
                    Console.WriteLine($"  {(backend.Value.GetProperty("ready").GetBoolean() ? "ready " : "absent")} {backend.Name,-18} {backend.Value.GetProperty("reason").GetString()}");
            }
            catch (HttpRequestException ex)
            {
                unreachable++;
                Console.WriteLine($"{kind} service {url}: not reachable ({ex.Message}). Start it: see tools/asset-services/README.md");
            }
        }
        return unreachable > 0 ? 1 : 0;
    }

    case "generate":
    {
        if (ReportPlanErrors())
            return 1;

        var todo = jobs
            .Where(j => force || AssetPlanner.StateOf(project, j) != AssetState.Approved)
            .Take(limit)
            .ToList();
        if (todo.Count == 0)
        {
            Console.WriteLine("Everything matching is approved and current. Use --force to regenerate.");
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) }; // AI backends on small GPUs are slow
        var client = new GeneratorClient(http);
        if (!await BackendsReady(client, project, todo))
            return 1;

        int generated = 0, approved = 0, failed = 0;
        foreach (var job in todo)
        {
            var set = !force ? AssetPipeline.LoadCandidates(project, job) : null;
            if (set is null)
            {
                try
                {
                    var started = DateTime.UtcNow;
                    set = await AssetPipeline.GenerateAsync(project, job, client);
                    generated++;
                    int passing = set.Candidates.Count(c => c.Errors.Count == 0);
                    Console.WriteLine($"  generated {job.AssetId}: {set.Candidates.Count} candidate(s), {passing} passing checks ({(DateTime.UtcNow - started).TotalSeconds:F1}s)");
                }
                catch (HttpRequestException ex)
                {
                    failed++;
                    Console.Error.WriteLine($"  FAILED {job.AssetId}: {ex.Message}");
                    continue;
                }
            }
            else if (!autoApprove)
            {
                Console.WriteLine($"  waiting  {job.AssetId}: candidates already generated (review them, or --force)");
                continue;
            }

            if (autoApprove)
            {
                var picks = set.Candidates.Where(c => c.Errors.Count == 0).Take(job.Variants).Select(c => c.Index).ToList();
                if (picks.Count == 0)
                {
                    Console.Error.WriteLine($"  not approved {job.AssetId}: no candidate passed the checks.");
                    continue;
                }
                AssetPipeline.Approve(project, job, picks, approvedBy, note ?? "auto-approved");
                approved++;
            }
        }

        Console.WriteLine($"Generated {generated}, auto-approved {approved}, failed {failed}.");
        if (!autoApprove || approved < todo.Count)
            Console.WriteLine($"Review: {ReviewPage.Write(project, plan.Jobs)}");
        return failed > 0 ? 1 : 0;
    }

    case "review":
        Console.WriteLine(ReviewPage.Write(project, plan.Jobs));
        return 0;

    case "approve":
    {
        if (positional.Count != 2)
        {
            Console.Error.WriteLine("Usage: approve <assetId> <n[,n…]>   e.g. approve sfx/weapon_titan/impact 2,5,1");
            return 2;
        }
        if (FindJob(positional[0]) is not { } job)
            return 1;
        try
        {
            var choices = positional[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
            var entry = AssetPipeline.Approve(project, job, choices, approvedBy, note, force);
            Console.WriteLine($"Approved {job.AssetId}: {string.Join(", ", entry.Files)}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    case "reject":
    {
        if (positional.Count != 1)
        {
            Console.Error.WriteLine("Usage: reject <assetId>");
            return 2;
        }
        if (FindJob(positional[0]) is not { } job)
            return 1;
        Console.WriteLine(AssetPipeline.Reject(project, job) ? $"Discarded candidates for {job.AssetId}." : $"No candidates waiting for {job.AssetId}.");
        return 0;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        return 2;
}

static async Task<bool> BackendsReady(GeneratorClient client, AssetProject project, IReadOnlyList<AssetJob> todo)
{
    bool ok = true;
    foreach (var group in todo.GroupBy(j => (Url: AssetPipeline.ServiceFor(project, j), j.Backend)))
    {
        try
        {
            var health = await client.HealthAsync(group.Key.Url);
            if (!health.GetProperty("backends").TryGetProperty(group.Key.Backend, out var backend))
            {
                Console.Error.WriteLine($"{group.Key.Url} has no backend '{group.Key.Backend}'.");
                ok = false;
            }
            else if (!backend.GetProperty("ready").GetBoolean())
            {
                Console.Error.WriteLine($"Backend '{group.Key.Backend}' is not ready: {backend.GetProperty("reason").GetString()}");
                ok = false;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.Error.WriteLine($"Generator service {group.Key.Url} is not reachable ({ex.Message}).");
            Console.Error.WriteLine("Start it: cd tools/asset-services; uv run python -m asset_services.server --kind image (or audio)");
            ok = false;
        }
    }
    return ok;
}
