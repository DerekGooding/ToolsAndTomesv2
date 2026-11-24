using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolsAndTomes.Assets;

// The pipeline's files under assets/ (docs/ASSET_PIPELINES.md). All JSON is camelCase and may contain comments.

public sealed record PipelineConfig
{
    public string ImageService { get; init; } = "http://127.0.0.1:8411";
    public string AudioService { get; init; } = "http://127.0.0.1:8412";
    public string ImageBackend { get; init; } = "procedural-image";
    public string AudioBackend { get; init; } = "procedural-audio";
    public string OutputRoot { get; init; } = "client/ToolsAndTomes.Client/assets";
    public IconSettings Icon { get; init; } = new();
    public SfxSettings Sfx { get; init; } = new();
}

public sealed record IconSettings
{
    public int Size { get; init; } = 256;
    public int Candidates { get; init; } = 4;
    public int Steps { get; init; } = 30;
    public double Guidance { get; init; } = 6.5;
    public int? RenderSize { get; init; }
}

public sealed record SfxSettings
{
    public int Candidates { get; init; } = 6;
    public int Variants { get; init; } = 3;
    public double DurationSeconds { get; init; } = 1.5;
    public int Steps { get; init; } = 100;
    public double Guidance { get; init; } = 7.0;
    public double TargetLufs { get; init; } = -16.0;
    public double MaxPeakDb { get; init; } = -1.0;
}

public sealed record StyleBible
{
    public int Version { get; init; }
    public string Name { get; init; } = "";
    public Dictionary<string, Palette> Palettes { get; init; } = [];
    public Dictionary<string, string> CategoryPalettes { get; init; } = [];
    public PromptTemplate Icon { get; init; } = new();
    public PromptTemplate Sfx { get; init; } = new();
}

public sealed record Palette
{
    public string Words { get; init; } = "";
    public List<string> Colors { get; init; } = [];
}

public sealed record PromptTemplate
{
    public string Prompt { get; init; } = "{subject}";
    public string NegativePrompt { get; init; } = "";
    public string? Background { get; init; }
}

public sealed record ModelRegistry
{
    public List<ModelEntry> Models { get; init; } = [];
}

public sealed record ModelEntry
{
    public required string Backend { get; init; }
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? Source { get; init; }
    public string License { get; init; } = "";
    public string? LicenseUrl { get; init; }
    public string? Checked { get; init; }

    /// <summary>approved, capped (commercial use only under a revenue cap), or rejected.</summary>
    public string Status { get; init; } = "rejected";

    public bool Placeholder { get; init; }
    public string? Notes { get; init; }

    [JsonIgnore]
    public bool Usable => Status is "approved" or "capped";
}

public sealed record SpecsFile
{
    public List<IconSpec> Icons { get; init; } = [];
    public List<SfxSpec> Sfx { get; init; } = [];
}

public sealed record IconSpec
{
    public required string Source { get; init; }
    public required string Prompt { get; init; }
    public string? Palette { get; init; }
    public int? Seed { get; init; }
}

public sealed record SfxSpec
{
    /// <summary>An action id, or "event:&lt;CombatEventType&gt;".</summary>
    public required string Source { get; init; }

    /// <summary>Slot (windup, impact, play) → prompt.</summary>
    public Dictionary<string, string> Slots { get; init; } = [];

    public double? DurationSeconds { get; init; }
    public int? Seed { get; init; }
}

/// <summary>assets/manifest.json: provenance of every approved asset (what, how, with which model and license).</summary>
public sealed record AssetManifest
{
    public SortedDictionary<string, ManifestEntry> Assets { get; init; } = new(StringComparer.Ordinal);
}

public sealed record ManifestEntry
{
    public required string Kind { get; init; }
    public required string Hash { get; init; }
    public required string Backend { get; init; }
    public required string Model { get; init; }
    public required string License { get; init; }
    public bool Placeholder { get; init; }
    public required string Prompt { get; init; }
    public string NegativePrompt { get; init; } = "";
    public required List<int> Seeds { get; init; }
    public required Dictionary<string, JsonElement> Params { get; init; }
    public required List<string> Files { get; init; }
    public List<Dictionary<string, JsonElement>> Metrics { get; init; } = [];
    public required string ApprovedAt { get; init; }
    public string? ApprovedBy { get; init; }
    public string? Note { get; init; }
}

/// <summary>A generated batch of candidates for one asset, stored outside the repo until one is approved.</summary>
public sealed record CandidateSet
{
    public required string AssetId { get; init; }
    public required string Hash { get; init; }
    public required string Backend { get; init; }
    public required string Model { get; init; }
    public required string Prompt { get; init; }
    public string NegativePrompt { get; init; } = "";
    public required Dictionary<string, JsonElement> Params { get; init; }
    public required string CreatedAt { get; init; }
    public List<Candidate> Candidates { get; init; } = [];
}

public sealed record Candidate
{
    public required int Index { get; init; }
    public required string File { get; init; }
    public required int Seed { get; init; }
    public Dictionary<string, JsonElement> Metrics { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

public static class AssetJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static T Read<T>(string path) where T : new()
    {
        if (!File.Exists(path))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options) + Environment.NewLine);
        File.Move(temp, path, overwrite: true);
    }

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
