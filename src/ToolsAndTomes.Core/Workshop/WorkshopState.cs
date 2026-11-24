using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Workshop;

public sealed record ResearchResult(bool Succeeded, string Message, IReadOnlyList<string> UnlockedComponentIds)
{
    public static ResearchResult Fail(string message) => new(false, message, []);
}

/// <summary>
/// The Inventor's Workshop progression: exotic material samples on hand, materials researched, and the
/// component blueprints that are therefore known. Known blueprints fabricate from fast-iron for free
/// (in the Workshop, supply is effectively unlimited because time is not a factor).
/// </summary>
public sealed class WorkshopState
{
    private readonly Dictionary<string, int> _samples = [];
    private readonly HashSet<string> _researched = [];
    private readonly HashSet<string> _completedMissions = [];

    public IReadOnlyDictionary<string, int> Samples => _samples;
    public IReadOnlySet<string> ResearchedMaterials => _researched;

    /// <summary>Missions completed at least once (they unlock later missions).</summary>
    public IReadOnlySet<string> CompletedMissions => _completedMissions;

    public bool HasCompleted(string missionId) => _completedMissions.Contains(missionId);

    public void MarkMissionCompleted(string missionId) => _completedMissions.Add(missionId);

    public int GetSamples(string materialId) => _samples.TryGetValue(materialId, out int n) ? n : 0;

    public void AddSamples(string materialId, int count)
    {
        if (count > 0)
            _samples[materialId] = GetSamples(materialId) + count;
    }

    public void AddSamples(IEnumerable<MaterialCount> rewards)
    {
        foreach (var r in rewards)
            AddSamples(r.MaterialId, r.Count);
    }

    public bool IsResearched(string materialId) => _researched.Contains(materialId);

    public bool CanResearch(ExoticMaterialDefinition material) =>
        !IsResearched(material.Id) && GetSamples(material.Id) >= material.SamplesToResearch;

    /// <summary>Consumes the required samples and permanently unlocks the material's blueprints.</summary>
    public ResearchResult Research(ExoticMaterialDefinition material)
    {
        if (IsResearched(material.Id))
            return ResearchResult.Fail($"{material.Name} is already researched.");

        int have = GetSamples(material.Id);
        if (have < material.SamplesToResearch)
            return ResearchResult.Fail($"{material.Name} needs {material.SamplesToResearch} samples (have {have}).");

        _samples[material.Id] = have - material.SamplesToResearch;
        if (_samples[material.Id] == 0)
            _samples.Remove(material.Id);
        _researched.Add(material.Id);

        return new ResearchResult(true, $"Researched {material.Name}.", material.UnlocksComponentIds);
    }

    /// <summary>Blueprints the Workshop can fabricate: starter-build components plus everything unlocked by research.</summary>
    public IReadOnlySet<string> KnownComponentIds(GameCatalog catalog)
    {
        var known = new HashSet<string>(catalog.BaselineComponentIds);
        foreach (var id in _researched)
        {
            if (catalog.Materials.TryGetValue(id, out var m))
                known.UnionWith(m.UnlocksComponentIds);
        }

        return known;
    }

    public bool CanFabricate(GameCatalog catalog, string componentId) =>
        catalog.Equipment.ContainsKey(componentId) && KnownComponentIds(catalog).Contains(componentId);

    /// <summary>Restores saved progress (used by profile loading).</summary>
    public void Restore(IReadOnlyDictionary<string, int> samples, IEnumerable<string> researched, IEnumerable<string>? completedMissions = null)
    {
        _completedMissions.Clear();
        _completedMissions.UnionWith(completedMissions ?? []);

        _samples.Clear();
        foreach (var (id, n) in samples)
            AddSamples(id, n);

        _researched.Clear();
        _researched.UnionWith(researched);
    }
}
