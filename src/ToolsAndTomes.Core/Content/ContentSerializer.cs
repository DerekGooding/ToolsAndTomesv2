using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Content;

/// <summary>
/// Reads and writes game content as JSON: <c>rules.json</c>, <c>actions.json</c>, <c>equipment.json</c>,
/// <c>configurations.json</c>, <c>enemies.json</c>, <c>materials.json</c>, and <c>encounters.json</c>. The shipped content is embedded in this assembly;
/// a directory with the same files can replace it (for modding, tools, or generated content).
/// All loaded content is validated by <see cref="ContentValidator"/>.
/// </summary>
public static class ContentSerializer
{
    public const string RulesFile = "rules.json";
    public const string ActionsFile = "actions.json";
    public const string EquipmentFile = "equipment.json";
    public const string ConfigurationsFile = "configurations.json";
    public const string EnemiesFile = "enemies.json";
    public const string MaterialsFile = "materials.json";
    public const string EncountersFile = "encounters.json";

    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyContentValues } },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Keeps content files short and hand-editable: zero numbers, false flags, nulls, and empty lists are omitted
    /// (they read back as their defaults). Enums, required members, and the rules file are always written in full.
    /// </summary>
    private static void OmitEmptyContentValues(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object ||
            typeInfo.Type == typeof(RulesConfig) ||
            typeInfo.Type == typeof(InterruptionThresholds))
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.IsRequired || property.PropertyType.IsEnum)
                continue;

            property.ShouldSerialize = static (_, value) => value switch
            {
                null => false,
                int i => i != 0,
                bool b => b,
                string s => s.Length > 0,
                System.Collections.IEnumerable e => e.GetEnumerator().MoveNext(),
                _ => true
            };
        }
    }

    /// <summary>Loads and validates the content shipped inside ToolsAndTomes.Core.</summary>
    public static GameCatalog LoadEmbedded()
    {
        var assembly = typeof(ContentSerializer).Assembly;
        return Load(name => ReadEmbedded(assembly, name));
    }

    /// <summary>Loads and validates content from a directory containing the seven content files.</summary>
    public static GameCatalog LoadFromDirectory(string directory) =>
        Load(name => File.ReadAllText(Path.Combine(directory, name)));

    /// <summary>Writes a catalog as the seven content files.</summary>
    public static void SaveToDirectory(GameCatalog catalog, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var (name, json) in Serialize(catalog))
            File.WriteAllText(Path.Combine(directory, name), json + Environment.NewLine);
    }

    public static IReadOnlyList<(string FileName, string Json)> Serialize(GameCatalog catalog) =>
    [
        (RulesFile, JsonSerializer.Serialize(catalog.Rules, Options)),
        (ActionsFile, JsonSerializer.Serialize(catalog.Actions.Values.ToList(), Options)),
        (EquipmentFile, JsonSerializer.Serialize(catalog.Equipment.Values.ToList(), Options)),
        (ConfigurationsFile, JsonSerializer.Serialize(catalog.StarterConfigurations.Values.ToList(), Options)),
        (EnemiesFile, JsonSerializer.Serialize(catalog.Enemies.Values.ToList(), Options)),
        (MaterialsFile, JsonSerializer.Serialize(catalog.Materials.Values.ToList(), Options)),
        (EncountersFile, JsonSerializer.Serialize(catalog.Encounters.Values.ToList(), Options))
    ];

    public static GameCatalog Load(Func<string, string> readFile)
    {
        var rules = Deserialize<RulesConfig>(readFile, RulesFile);
        var actions = Deserialize<List<ActionDefinition>>(readFile, ActionsFile);
        var equipment = Deserialize<List<EquipmentDefinition>>(readFile, EquipmentFile);
        var configurations = Deserialize<List<CrestConfiguration>>(readFile, ConfigurationsFile);
        var enemies = Deserialize<List<EnemyDefinition>>(readFile, EnemiesFile);
        var materials = Deserialize<List<ExoticMaterialDefinition>>(readFile, MaterialsFile);
        var encounters = Deserialize<List<EncounterDefinition>>(readFile, EncountersFile);

        IReadOnlyList<string> errors = ContentValidator.FindDuplicateIds(actions, equipment, configurations, enemies, materials, encounters);
        if (errors.Count > 0)
            throw new ContentValidationException(errors);

        var catalog = new GameCatalog(
            actions.ToDictionary(a => a.Id),
            equipment.ToDictionary(e => e.Id),
            configurations.ToDictionary(c => c.Id),
            enemies.ToDictionary(e => e.Id),
            rules,
            materials.ToDictionary(m => m.Id),
            encounters.ToDictionary(e => e.Id));

        errors = ContentValidator.Validate(catalog);
        if (errors.Count > 0)
            throw new ContentValidationException(errors);

        return catalog;
    }

    private static T Deserialize<T>(Func<string, string> readFile, string fileName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(readFile(fileName), Options)
                   ?? throw new ContentValidationException([$"{fileName}: file is empty."]);
        }
        catch (JsonException ex)
        {
            throw new ContentValidationException([$"{fileName}: {ex.Message}"]);
        }
    }

    private static string ReadEmbedded(Assembly assembly, string fileName)
    {
        string resource = $"ToolsAndTomes.Content.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"Embedded content '{resource}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public sealed class ContentValidationException(IReadOnlyList<string> errors)
    : Exception("Invalid game content:\n  " + string.Join("\n  ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
