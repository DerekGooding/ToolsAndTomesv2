using System.Text.Json;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Workshop;

/// <summary>
/// Saved player progress: Crest assemblies (component order = Damage Order, plus reaction rules), spare
/// components, and Workshop research. Component damage is not saved: the Workshop repairs everything.
/// </summary>
public sealed record PlayerProfile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public string? SelectedAssemblyId { get; init; }
    public IReadOnlyList<CrestConfiguration> Assemblies { get; init; } = [];
    public IReadOnlyList<string> SpareComponentIds { get; init; } = [];
    public IReadOnlyDictionary<string, int> Samples { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> ResearchedMaterials { get; init; } = [];
    public int EncountersWon { get; init; }
}

public sealed record ProfileLoadResult(PlayerProfile Profile, IReadOnlyList<string> Warnings);

public static class ProfileSerializer
{
    public static string Serialize(PlayerProfile profile) =>
        JsonSerializer.Serialize(profile, ContentSerializer.Options);

    /// <summary>
    /// Reads a profile and reconciles it with the current content: unknown components, materials, and reaction
    /// rules are dropped (with warnings) so a profile saved against older content still loads.
    /// </summary>
    public static ProfileLoadResult Deserialize(string json, GameCatalog catalog)
    {
        var raw = JsonSerializer.Deserialize<PlayerProfile>(json, ContentSerializer.Options)
                  ?? throw new JsonException("Profile is empty.");
        var warnings = new List<string>();

        bool KnownComponent(string id, string where)
        {
            if (catalog.Equipment.ContainsKey(id))
                return true;
            warnings.Add($"Dropped unknown component '{id}' from {where}.");
            return false;
        }

        var assemblies = raw.Assemblies
            .Select(a => a with
            {
                ComponentIds = a.ComponentIds.Where(id => KnownComponent(id, a.Name)).ToList(),
                ReactionRules = a.ReactionRules.Where(r =>
                {
                    bool ok = catalog.Actions.TryGetValue(r.ActionId, out var def) && def.IsReactive;
                    if (!ok) warnings.Add($"Dropped reaction rule '{r.ActionId}' from {a.Name}.");
                    return ok;
                }).ToList(),
                Cargo = a.Cargo.Where(c =>
                {
                    bool ok = c.Kind != CargoKind.Consumable || (c.ItemId is not null && catalog.Consumables.ContainsKey(c.ItemId));
                    if (!ok) warnings.Add($"Dropped unknown cargo item '{c.ItemId}' from {a.Name}.");
                    return ok;
                }).ToList()
            })
            .ToList();

        var spares = raw.SpareComponentIds.Where(id => KnownComponent(id, "spares")).ToList();

        var samples = raw.Samples
            .Where(kv =>
            {
                bool ok = catalog.Materials.ContainsKey(kv.Key);
                if (!ok) warnings.Add($"Dropped samples of unknown material '{kv.Key}'.");
                return ok;
            })
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var researched = raw.ResearchedMaterials.Where(catalog.Materials.ContainsKey).ToList();

        return new ProfileLoadResult(
            raw with { Assemblies = assemblies, SpareComponentIds = spares, Samples = samples, ResearchedMaterials = researched },
            warnings);
    }
}
