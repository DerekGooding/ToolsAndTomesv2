using System.IO;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;

namespace ToolsAndTomes.App;

public sealed class AppSessionState
{
    /// <summary>Saved progress lives in %LOCALAPPDATA%\ToolsAndTomes\profile.json.</summary>
    public static string ProfilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsAndTomes", "profile.json");

    private int _nextInstanceCounter = 100;
    private int _nextAssemblyCounter = 1;
    private CombatSimulation? _rewardedSimulation;

    public GameCatalog Catalog { get; } = GameCatalog.Default;
    public List<CrestLoadout> CrestAssemblies { get; } = [];
    public List<EquipmentInstance> SpareInventory { get; } = [];
    public WorkshopState Workshop { get; } = new();
    public string SelectedCrestId { get; set; } = "";
    public int EncountersWon { get; private set; }

    /// <summary>Messages from loading the profile (e.g. content that no longer exists).</summary>
    public IReadOnlyList<string> ProfileWarnings { get; private set; } = [];

    public CombatSimulation? ActiveSimulation { get; private set; }
    public EnemyAIController ActiveAI { get; } = new(decisionIntervalBeats: 1);
    public int EncounterSeed { get; set; } = 1337;

    /// <summary>Samples granted by the most recent victory (shown on the combat screen).</summary>
    public IReadOnlyList<MaterialCount> LastRewards { get; private set; } = [];

    public CrestLoadout SelectedLoadout =>
        CrestAssemblies.First(c => c.Id == SelectedCrestId);

    public AppSessionState()
    {
        if (!TryLoadProfile())
            CreateNewProfile();
    }

    // ---- Assemblies & spares ----------------------------------------------------------------

    public EquipmentInstance CreateNewInstance(string equipmentDefId)
    {
        var def = Catalog.GetEquipment(equipmentDefId);
        return new EquipmentInstance($"inv_{_nextInstanceCounter++}_{def.Id}", def);
    }

    public void AddSpare(string equipmentDefId) => SpareInventory.Add(CreateNewInstance(equipmentDefId));

    /// <summary>Fabricates a known blueprint from fast-iron (free in the Workshop).</summary>
    public bool Fabricate(string equipmentDefId)
    {
        if (!Workshop.CanFabricate(Catalog, equipmentDefId))
            return false;

        AddSpare(equipmentDefId);
        return true;
    }

    public CrestLoadout SaveCurrentAsNewAssembly(string name)
    {
        string id;
        do
        {
            id = $"custom_asm_{_nextAssemblyCounter++}";
        } while (CrestAssemblies.Any(a => a.Id == id));

        var blueprint = SelectedLoadout.ExportConfiguration(id, $"Saved from {SelectedLoadout.Name}");
        blueprint = blueprint with { Name = string.IsNullOrWhiteSpace(name) ? $"Custom Assembly {_nextAssemblyCounter}" : name.Trim() };

        var freshAssembly = CrestLoadout.CreateFromConfiguration(blueprint, Catalog, id);
        CrestAssemblies.Add(freshAssembly);
        SelectedCrestId = freshAssembly.Id;
        return freshAssembly;
    }

    public bool UninstallFromSelected(string instanceId)
    {
        var removed = SelectedLoadout.Uninstall(instanceId);
        if (removed is null)
            return false;

        SpareInventory.Add(removed);
        return true;
    }

    public bool InstallFromSpare(string instanceId)
    {
        var item = SpareInventory.FirstOrDefault(e => e.InstanceId == instanceId);
        if (item is null)
            return false;

        SpareInventory.Remove(item);
        SelectedLoadout.Install(item);
        return true;
    }

    public bool TransferToOtherAssembly(string instanceId, string targetAssemblyId)
    {
        var target = CrestAssemblies.FirstOrDefault(c => c.Id == targetAssemblyId);
        if (target is null)
            return false;

        return SelectedLoadout.TransferComponentTo(instanceId, target);
    }

    // ---- Research ---------------------------------------------------------------------------

    public ResearchResult Research(string materialId)
    {
        var result = Workshop.Research(Catalog.Materials[materialId]);
        if (result.Succeeded)
            Save();
        return result;
    }

    // ---- Missions ---------------------------------------------------------------------------

    /// <summary>The mission in progress, if any.</summary>
    public MissionRun? ActiveMission { get; private set; }

    /// <summary>A one-line summary of the last finished mission (shown in the Crest Bay).</summary>
    public string? LastMissionReport { get; private set; }

    public IEnumerable<MissionDefinition> AvailableMissions =>
        Catalog.Missions.Values.Where(m => MissionRun.IsUnlocked(m, Workshop));

    /// <summary>Starts a mission with the selected assembly (the Workshop repairs and restocks it first).</summary>
    public MissionRun StartMission(string missionId)
    {
        var mission = Catalog.Missions[missionId];
        if (!MissionRun.IsUnlocked(mission, Workshop))
            throw new InvalidOperationException($"{mission.Name} is locked.");

        LastRewards = [];
        LastMissionReport = null;
        ActiveMission = MissionRun.Start(mission, SelectedLoadout, Catalog, EncounterSeed, Workshop.KnownComponentIds(Catalog));
        EncounterSeed++; // the next mission rolls differently
        return ActiveMission;
    }

    /// <summary>Starts the mission's current encounter, paused.</summary>
    public CombatSimulation StartEncounter()
    {
        var run = ActiveMission ?? throw new InvalidOperationException("No mission in progress.");
        LastRewards = [];
        ActiveSimulation = run.BeginEncounter();
        ActiveSimulation.Pause();
        return ActiveSimulation;
    }

    /// <summary>
    /// Settles a finished encounter once: banks its rewards into the Workshop immediately (so they survive a later
    /// defeat), and records the mission's result if it ended. Returns the samples granted by this encounter.
    /// </summary>
    public IReadOnlyList<MaterialCount> CompleteEncounter(CombatSimulation sim)
    {
        if (ReferenceEquals(sim, _rewardedSimulation) || sim.Outcome == EncounterOutcome.InProgress)
            return LastRewards;

        _rewardedSimulation = sim;
        var run = ActiveMission;
        if (run is null || !ReferenceEquals(run.ActiveSimulation, sim))
            return LastRewards;

        LastRewards = run.FinishEncounter();
        Workshop.AddSamples(LastRewards);
        if (sim.Outcome == EncounterOutcome.PlayerVictory)
            EncountersWon++;

        if (run.IsOver)
            EndMission(run);

        Save();
        return LastRewards;
    }

    /// <summary>Leaves the mission (mid-fight or during a rest); samples already banked are kept.</summary>
    public void WithdrawFromMission()
    {
        if (ActiveMission is not { } run || run.IsOver)
            return;

        run.Withdraw();
        EndMission(run);
        Save();
    }

    private void EndMission(MissionRun run)
    {
        int samples = run.RewardsEarned.Sum(r => r.Count);
        string outcome = run.Phase switch
        {
            MissionPhase.Completed => "completed",
            MissionPhase.Failed => $"failed at encounter {run.StageIndex + 1}",
            _ => $"withdrawn after {run.StageIndex} encounter(s)"
        };
        LastMissionReport = $"{run.Mission.Name} {outcome}: {samples} sample(s) recovered.";
        ActiveMission = null;
        ActiveSimulation = null;
    }

    // ---- Profile ----------------------------------------------------------------------------

    public void Save()
    {
        var profile = new PlayerProfile
        {
            SelectedAssemblyId = SelectedCrestId,
            Assemblies = CrestAssemblies.Select(a => a.ExportConfiguration()).ToList(),
            SpareComponentIds = SpareInventory.Select(e => e.Definition.Id).ToList(),
            Samples = Workshop.Samples.ToDictionary(),
            ResearchedMaterials = Workshop.ResearchedMaterials.Order(StringComparer.Ordinal).ToList(),
            EncountersWon = EncountersWon
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(ProfilePath, ProfileSerializer.Serialize(profile));
        }
        catch (IOException)
        {
            // Saving is best-effort; the session continues in memory.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Discards saved progress and starts over with the starter builds.</summary>
    public void ResetProfile()
    {
        CrestAssemblies.Clear();
        SpareInventory.Clear();
        Workshop.Restore(new Dictionary<string, int>(), []);
        EncountersWon = 0;
        EncounterSeed = 1337;
        ActiveMission = null;
        ActiveSimulation = null;
        LastMissionReport = null;
        ProfileWarnings = [];
        CreateNewProfile();
        Save();
    }

    private bool TryLoadProfile()
    {
        if (!File.Exists(ProfilePath))
            return false;

        try
        {
            var loaded = ProfileSerializer.Deserialize(File.ReadAllText(ProfilePath), Catalog);
            var profile = loaded.Profile;
            if (profile.Assemblies.Count == 0)
                return false;

            int index = 1;
            foreach (var config in profile.Assemblies)
                CrestAssemblies.Add(CrestLoadout.CreateFromConfiguration(config, Catalog, $"asm{index++}"));

            foreach (var id in profile.SpareComponentIds)
                AddSpare(id);

            Workshop.Restore(profile.Samples, profile.ResearchedMaterials);
            EncountersWon = profile.EncountersWon;
            EncounterSeed = 1337 + EncountersWon;
            SelectedCrestId = CrestAssemblies.Any(a => a.Id == profile.SelectedAssemblyId)
                ? profile.SelectedAssemblyId!
                : CrestAssemblies[0].Id;
            _nextAssemblyCounter = CrestAssemblies.Count + 1;
            ProfileWarnings = loaded.Warnings;
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            ProfileWarnings = [$"Saved profile could not be read ({ex.Message}); starting fresh."];
            CrestAssemblies.Clear();
            SpareInventory.Clear();
            return false;
        }
    }

    private void CreateNewProfile()
    {
        var agile = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, Catalog, "asm1");
        var heavy = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, Catalog, "asm2");
        CrestAssemblies.Add(agile);
        CrestAssemblies.Add(heavy);
        SelectedCrestId = agile.Id;

        // A few spare starter-build parts to experiment with; everything else comes from research.
        foreach (var id in new[]
                 {
                     "core_aether_dynamo", "core_aether_furnace", "mob_stride_legs", "mob_anchor_legs",
                     "eq_dragons_breath", "eq_shield_generator", "eq_repulsor", "eq_blink_drive",
                     "armor_heavy_plate", "eq_thermal_regulator", "eq_spell_amplifier", "util_cargo_rack"
                 })
        {
            AddSpare(id);
        }
    }
}
