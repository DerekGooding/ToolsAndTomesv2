using System.Net.Http.Json;
using System.Text.Json;

namespace ToolsAndTomes.Assets;

public sealed record GenerateRequest(
    string Backend,
    string Prompt,
    string NegativePrompt,
    int Seed,
    int Count,
    IReadOnlyList<string> Palette,
    IReadOnlyDictionary<string, object> Params);

public sealed record GeneratedCandidate(int Seed, string Format, string Data, Dictionary<string, JsonElement>? Metrics);

public sealed record GenerateResponse(string Backend, string Model, List<GeneratedCandidate> Candidates);

/// <summary>Talks to a generator service (tools/asset-services). Files come back in the response body.</summary>
public sealed class GeneratorClient(HttpClient http)
{
    public async Task<JsonElement> HealthAsync(string serviceUrl, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<JsonElement>($"{serviceUrl.TrimEnd('/')}/health", AssetJson.Options, ct);

    public async Task<GenerateResponse> GenerateAsync(string serviceUrl, GenerateRequest request, CancellationToken ct = default)
    {
        // Buffered (not chunked): the services' stdlib HTTP server reads bodies by Content-Length.
        using var content = new StringContent(JsonSerializer.Serialize(request, AssetJson.Options), System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{serviceUrl.TrimEnd('/')}/generate", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            string message = body;
            try
            {
                message = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() ?? body;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // not the service's JSON error shape: report the raw body
            }
            throw new HttpRequestException($"{(int)response.StatusCode} from {serviceUrl}: {message}");
        }

        return await response.Content.ReadFromJsonAsync<GenerateResponse>(AssetJson.Options, ct)
               ?? throw new HttpRequestException($"Empty response from {serviceUrl}.");
    }
}

/// <summary>Generate → review → approve, with every approval recorded in assets/manifest.json.</summary>
public static class AssetPipeline
{
    public const string CandidateSetFile = "set.json";

    public static string ServiceFor(AssetProject project, AssetJob job) =>
        job.Kind == AssetKind.Icon ? project.Config.ImageService : project.Config.AudioService;

    public static async Task<CandidateSet> GenerateAsync(AssetProject project, AssetJob job, GeneratorClient client, CancellationToken ct = default)
    {
        var response = await client.GenerateAsync(ServiceFor(project, job), new GenerateRequest(
            job.Backend, job.Prompt, job.NegativePrompt, job.Seed, job.Candidates, job.Palette, job.Params), ct);

        string dir = project.CandidateDir(job);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var candidates = new List<Candidate>();
        for (int i = 0; i < response.Candidates.Count; i++)
        {
            var generated = response.Candidates[i];
            string file = $"candidate_{i + 1}.{generated.Format}";
            await File.WriteAllBytesAsync(Path.Combine(dir, file), Convert.FromBase64String(generated.Data), ct);
            var metrics = generated.Metrics ?? [];
            var (warnings, errors) = AssetChecks.Evaluate(project.Config, job, metrics);
            candidates.Add(new Candidate { Index = i + 1, File = file, Seed = generated.Seed, Metrics = metrics, Warnings = warnings, Errors = errors });
        }

        var set = new CandidateSet
        {
            AssetId = job.AssetId,
            Hash = job.Hash,
            Backend = response.Backend,
            Model = response.Model,
            Prompt = job.Prompt,
            NegativePrompt = job.NegativePrompt,
            Params = ToElements(job.Params),
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Candidates = candidates
        };
        AssetJson.Write(Path.Combine(dir, CandidateSetFile), set);
        return set;
    }

    /// <summary>The candidates generated for the job's current hash, if any.</summary>
    public static CandidateSet? LoadCandidates(AssetProject project, AssetJob job)
    {
        string path = Path.Combine(project.CandidateDir(job), CandidateSetFile);
        return File.Exists(path) ? JsonSerializer.Deserialize<CandidateSet>(File.ReadAllText(path), AssetJson.Options) : null;
    }

    /// <summary>
    /// Copies the chosen candidates into the game's asset folder (icons: exactly one; sounds: up to the variant
    /// count, in the order given) and records the provenance in the manifest.
    /// </summary>
    public static ManifestEntry Approve(AssetProject project, AssetJob job, IReadOnlyList<int> choices, string? approvedBy = null,
                                        string? note = null, bool force = false)
    {
        var set = LoadCandidates(project, job)
                  ?? throw new InvalidOperationException($"{job.AssetId}: no candidates for the current spec. Run `generate` first.");

        if (choices.Count == 0)
            throw new ArgumentException($"{job.AssetId}: choose at least one candidate.");
        if (choices.Count > job.Variants)
            throw new ArgumentException($"{job.AssetId}: at most {job.Variants} candidate(s) can be approved.");
        if (choices.Distinct().Count() != choices.Count)
            throw new ArgumentException($"{job.AssetId}: a candidate was chosen twice.");

        var chosen = choices.Select(n => set.Candidates.FirstOrDefault(c => c.Index == n)
                                         ?? throw new ArgumentException($"{job.AssetId}: no candidate {n} (have 1-{set.Candidates.Count}).")).ToList();
        var failing = chosen.Where(c => c.Errors.Count > 0).ToList();
        if (failing.Count > 0 && !force)
            throw new InvalidOperationException(
                $"{job.AssetId}: candidate {string.Join(", ", failing.Select(c => c.Index))} failed checks " +
                $"({string.Join("; ", failing.SelectMany(c => c.Errors))}). Pass --force to approve anyway.");

        var model = project.FindModel(set.Backend);
        var files = new List<string>();
        for (int i = 0; i < chosen.Count; i++)
        {
            string target = Path.Combine(project.OutputRoot, job.OutputFiles[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(project.CandidateDir(job), chosen[i].File), target, overwrite: true);
            files.Add(job.OutputFiles[i]);
        }

        // Fewer variants than before: remove the leftovers so the game doesn't play stale sounds.
        foreach (string leftover in job.OutputFiles.Skip(chosen.Count))
        {
            string path = Path.Combine(project.OutputRoot, leftover);
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".import"))
                File.Delete(path + ".import");
        }

        var entry = new ManifestEntry
        {
            Kind = job.Kind.ToString(),
            Hash = job.Hash,
            Backend = set.Backend,
            Model = set.Model,
            License = model?.License ?? "unknown",
            Placeholder = model?.Placeholder ?? false,
            Prompt = set.Prompt,
            NegativePrompt = set.NegativePrompt,
            Seeds = chosen.Select(c => c.Seed).ToList(),
            Params = set.Params,
            Files = files,
            Metrics = chosen.Select(c => c.Metrics).ToList(),
            ApprovedAt = DateTimeOffset.UtcNow.ToString("O"),
            ApprovedBy = approvedBy,
            Note = note
        };
        project.Manifest.Assets[job.AssetId] = entry;
        AssetJson.Write(project.ManifestPath, project.Manifest);
        return entry;
    }

    /// <summary>Discards the candidates for the job's current hash.</summary>
    public static bool Reject(AssetProject project, AssetJob job)
    {
        string dir = project.CandidateDir(job);
        if (!Directory.Exists(dir))
            return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    private static Dictionary<string, JsonElement> ToElements(IReadOnlyDictionary<string, object> values) =>
        values.ToDictionary(kv => kv.Key, kv => AssetJson.ToElement(kv.Value));
}

/// <summary>Automatic checks on generated candidates (docs/ASSET_PIPELINES.md §7).</summary>
public static class AssetChecks
{
    public const double LoudnessTolerance = 3.0;

    public static (List<string> Warnings, List<string> Errors) Evaluate(PipelineConfig config, AssetJob job, IReadOnlyDictionary<string, JsonElement> metrics)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (job.Kind == AssetKind.Icon)
        {
            if (Bool(metrics, "subjectFound") == false)
                errors.Add("no subject could be separated from the background");
            if (Bool(metrics, "touchesEdge") == true)
                warnings.Add("subject touches the image edge (may be cropped)");
            if (Number(metrics, "coverage") is { } coverage && (coverage < 0.08 || coverage > 0.85))
                warnings.Add($"subject covers {coverage:P0} of the icon (expected 8-85%)");
            if (Number(metrics, "width") is { } w && (int)w != config.Icon.Size)
                errors.Add($"icon is {w}px, expected {config.Icon.Size}px");
        }
        else
        {
            if (Bool(metrics, "silent") == true || Number(metrics, "lufs") is null)
                errors.Add("silent");
            else if (Number(metrics, "lufs") is { } lufs && Math.Abs(lufs - config.Sfx.TargetLufs) > LoudnessTolerance)
                warnings.Add($"loudness {lufs:F1} LUFS (target {config.Sfx.TargetLufs:F0} ±{LoudnessTolerance:F0})");
            if (Number(metrics, "peakDb") is { } peak && peak > config.Sfx.MaxPeakDb + 0.1)
                errors.Add($"peak {peak:F1} dBFS above the {config.Sfx.MaxPeakDb:F0} dBFS ceiling");
            if (Number(metrics, "durationSeconds") is { } seconds && seconds < 0.05)
                errors.Add($"only {seconds * 1000:F0} ms long");
        }

        return (warnings, errors);
    }

    private static double? Number(IReadOnlyDictionary<string, JsonElement> metrics, string key) =>
        metrics.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool? Bool(IReadOnlyDictionary<string, JsonElement> metrics, string key) =>
        metrics.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}
