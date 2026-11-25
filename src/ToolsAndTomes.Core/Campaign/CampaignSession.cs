using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;

namespace ToolsAndTomes.Core.Campaign;

/// <summary>A Crest the player can refit: one of their assemblies, or an allied pilot's Crest.</summary>
public sealed record RefitTarget(string Label, CrestLoadout Loadout, string? PilotId);

/// <summary>
/// The player's campaign, shared by every frontend (the WPF tool and the Godot client): Crest assemblies and
/// spares, Workshop research, allied pilots and the lance, the mission in progress, and the saved profile.
/// Frontends call into it and draw what it holds; it has no UI.
/// </summary>
public sealed class CampaignSession
{
    /// <summary>The default profile: %LOCALAPPDATA%\ToolsAndTomes\profile.json (shared by both frontends).</summary>
    public static string DefaultProfilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsAndTomes", "profile.json");

    /// <summary>Where this session loads and saves its profile.</summary>
    public string ProfilePath { get; }

    private int _nextInstanceCounter = 100;
    private int _nextAssemblyCounter = 1;
    private CombatSimulation? _rewardedSimulation;

    /// <summary>The last between-fights snapshot of the active mission (kept while a fight is in progress).</summary>
    private MissionSave? _missionSave;

    public GameCatalog Catalog { get; } = GameCatalog.Default;
    public List<CrestLoadout> CrestAssemblies { get; } = [];
    public List<EquipmentInstance> SpareInventory { get; } = [];
    public WorkshopState Workshop { get; } = new();
    public string SelectedCrestId { get; set; } = "";
    public int EncountersWon { get; private set; }

    /// <summary>Messages from loading the profile (e.g. content that no longer exists).</summary>
    public IReadOnlyList<string> ProfileWarnings { get; private set; } = [];

    public CombatSimulation? ActiveSimulation { get; private set; }
    public EnemyAIController ActiveAI { get; private set; } = new(decisionIntervalBeats: 1);
    public AllyAIController AllyAI { get; } = new();

    /// <summary>Each allied pilot's Crest, kept across missions (refit in the Workshop; repaired before each mission).</summary>
    public Dictionary<string, CrestLoadout> PilotCrests { get; } = [];

    /// <summary>Pilot ids who join the player on missions, in lance order (at most <see cref="MissionRun.MaxAllies"/>).</summary>
    public List<string> Lance { get; } = [];
    public int EncounterSeed { get; set; } = 1337;

    /// <summary>Samples granted by the most recent victory (shown on the combat screen).</summary>
    public IReadOnlyList<MaterialCount> LastRewards { get; private set; } = [];

    public CrestLoadout SelectedLoadout =>
        CrestAssemblies.First(c => c.Id == SelectedCrestId);

    /// <param name="profilePath">The profile file (default <see cref="DefaultProfilePath"/>); e.g. a temporary file for automated runs.</param>
    public CampaignSession(string? profilePath = null)
    {
        ProfilePath = profilePath ?? DefaultProfilePath;
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

    public bool UninstallFromSelected(string instanceId) => Uninstall(SelectedLoadout, instanceId);

    public bool InstallFromSpare(string instanceId) => InstallSpare(SelectedLoadout, instanceId);

    public bool TransferToOtherAssembly(string instanceId, string targetAssemblyId) =>
        CrestAssemblies.FirstOrDefault(c => c.Id == targetAssemblyId) is { } target &&
        Transfer(SelectedLoadout, instanceId, target);

    // ---- Refitting any Crest (your assemblies and the pilots' Crests) ---------------------------

    /// <summary>Refitting happens in the Workshop, between missions (a mission's Crests are in the field).</summary>
    public bool CanRefit => ActiveMission is null;

    /// <summary>Every Crest the player can refit: their own assemblies, then each pilot's Crest.</summary>
    public IReadOnlyList<RefitTarget> RefitTargets =>
        CrestAssemblies.Select(a => new RefitTarget(a.Name, a, null))
            .Concat(Catalog.Pilots.Values
                .Where(p => PilotCrests.ContainsKey(p.Id))
                .Select(p => new RefitTarget($"{p.DisplayName}'s {PilotCrests[p.Id].Name}", PilotCrests[p.Id], p.Id)))
            .ToList();

    /// <summary>Moves an installed component into the spares inventory.</summary>
    public bool Uninstall(CrestLoadout from, string instanceId)
    {
        if (!CanRefit || from.Uninstall(instanceId) is not { } removed)
            return false;

        SpareInventory.Add(removed);
        return true;
    }

    /// <summary>Installs a spare (non-Core parts go before the Core in the Damage Order).</summary>
    public bool InstallSpare(CrestLoadout into, string instanceId)
    {
        if (!CanRefit || SpareInventory.FirstOrDefault(e => e.InstanceId == instanceId) is not { } item)
            return false;
        if (!into.Install(item))
            return false; // an identical instance is already installed; the spare stays in inventory

        SpareInventory.Remove(item);
        return true;
    }

    /// <summary>Moves an installed component straight from one Crest to another.</summary>
    public bool Transfer(CrestLoadout from, string instanceId, CrestLoadout to) =>
        CanRefit && from.TransferComponentTo(instanceId, to);

    /// <summary>The Workshop repairs and restocks for free (missions start repaired anyway).</summary>
    public void RepairInWorkshop(CrestLoadout crest)
    {
        crest.RepairAll();
        foreach (var spare in SpareInventory)
        {
            spare.Repair();
            spare.RefillAmmo();
        }
    }

    // ---- Research ---------------------------------------------------------------------------

    /// <summary>Picks the assembly to fly on the next mission (not while a mission is in progress).</summary>
    public bool SelectCrest(string assemblyId)
    {
        if (ActiveMission is not null || CrestAssemblies.All(a => a.Id != assemblyId))
            return false;
        SelectedCrestId = assemblyId;
        Save();
        return true;
    }

    /// <summary>
    /// Sets who joins the player on the next mission, in lance order: known pilots, no repeats, at most
    /// <see cref="MissionRun.MaxAllies"/> (not while a mission is in progress).
    /// </summary>
    public bool SetLance(IEnumerable<string> pilotIds)
    {
        var ids = pilotIds.ToList();
        if (ActiveMission is not null || ids.Count > MissionRun.MaxAllies || ids.Distinct().Count() != ids.Count ||
            ids.Any(id => !Catalog.Pilots.ContainsKey(id)))
            return false;

        Lance.Clear();
        Lance.AddRange(ids);
        Save();
        return true;
    }

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
        var first = Catalog.Encounters[mission.EncounterIds[0]];
        var allies = Lance
            .Take(MissionRun.MaxAllies)
            .Select(id => EncounterFactory.CreateAlly(Catalog.Pilots[id], Catalog, PilotCrests[id], first.PlayerStartingMotes))
            .ToList();
        ActiveMission = MissionRun.Start(mission, SelectedLoadout, Catalog, EncounterSeed, Workshop.KnownComponentIds(Catalog), allies);
        EncounterSeed++; // the next mission rolls differently
        Save();
        return ActiveMission;
    }

    /// <summary>Starts the mission's current encounter, paused.</summary>
    public CombatSimulation StartEncounter()
    {
        var run = ActiveMission ?? throw new InvalidOperationException("No mission in progress.");
        LastRewards = [];
        Save(); // quitting mid-fight resumes here, at the start of this fight
        ActiveSimulation = run.BeginEncounter();
        ActiveSimulation.Pause();
        ActiveAI = new EnemyAIController(decisionIntervalBeats: 1); // fresh target memory per fight
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
        if (run.Phase == MissionPhase.Completed)
            Workshop.MarkMissionCompleted(run.Mission.Id);

        // Back at the Workshop: every Crest is repaired and restocked (damage is never kept between missions).
        foreach (var crest in run.Lance)
            crest.Loadout.RepairAll();

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
        _missionSave = null;
    }

    // ---- Profile ----------------------------------------------------------------------------

    /// <summary>Saves the profile, including the active mission's latest between-fights snapshot.</summary>
    public void Save()
    {
        if (ActiveMission is null)
            _missionSave = null;
        else if (ActiveMission.CanSave)
            _missionSave = ActiveMission.CreateSave();

        var profile = new PlayerProfile
        {
            SelectedAssemblyId = SelectedCrestId,
            Assemblies = CrestAssemblies.Select(a => a.ExportConfiguration()).ToList(),
            SpareComponentIds = SpareInventory.Select(e => e.Definition.Id).ToList(),
            Samples = Workshop.Samples.ToDictionary(),
            ResearchedMaterials = Workshop.ResearchedMaterials.Order(StringComparer.Ordinal).ToList(),
            CompletedMissions = Workshop.CompletedMissions.Order(StringComparer.Ordinal).ToList(),
            EncountersWon = EncountersWon,
            Pilots = PilotCrests
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new PilotRecord { PilotId = kv.Key, Assembly = kv.Value.ExportConfiguration() })
                .ToList(),
            Lance = Lance.ToList(),
            ActiveMission = _missionSave
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
        PilotCrests.Clear();
        Lance.Clear();
        Workshop.Restore(new Dictionary<string, int>(), []);
        EncountersWon = 0;
        EncounterSeed = 1337;
        ActiveMission = null;
        ActiveSimulation = null;
        _missionSave = null;
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

            Workshop.Restore(profile.Samples, profile.ResearchedMaterials, profile.CompletedMissions);
            EncountersWon = profile.EncountersWon;
            EncounterSeed = 1337 + EncountersWon;
            SelectedCrestId = CrestAssemblies.Any(a => a.Id == profile.SelectedAssemblyId)
                ? profile.SelectedAssemblyId!
                : CrestAssemblies[0].Id;
            _nextAssemblyCounter = CrestAssemblies.Count + 1;

            foreach (var record in profile.Pilots)
                PilotCrests[record.PilotId] = CrestLoadout.CreateFromConfiguration(record.Assembly, Catalog, record.PilotId);
            Lance.AddRange(profile.Lance.Take(MissionRun.MaxAllies));
            EnsureRoster(fillLance: profile.Pilots.Count == 0 && profile.Lance.Count == 0);

            var warnings = loaded.Warnings.ToList();
            if (profile.ActiveMission is { } saved)
                ResumeMission(saved, warnings);

            ProfileWarnings = warnings;
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            ProfileWarnings = [$"Saved profile could not be read ({ex.Message}); starting fresh."];
            CrestAssemblies.Clear();
            SpareInventory.Clear();
            PilotCrests.Clear();
            Lance.Clear();
            return false;
        }
    }

    /// <summary>
    /// Rebuilds a saved mission. The mission's Crest replaces the assembly it was launched with (it carries the
    /// mission's damage and shaped parts), and allied pilots' Crests are taken from the save.
    /// </summary>
    private void ResumeMission(MissionSave saved, List<string> warnings)
    {
        var run = MissionRun.TryResume(saved, Catalog, Workshop.KnownComponentIds(Catalog), out var error);
        if (run is null)
        {
            warnings.Add($"The mission in progress could not be resumed ({error}); it was abandoned. Banked samples are kept.");
            return;
        }

        int index = CrestAssemblies.FindIndex(a => a.Id == run.Loadout.Id);
        if (index >= 0)
            CrestAssemblies[index] = run.Loadout;
        else
            CrestAssemblies.Add(run.Loadout);
        SelectedCrestId = run.Loadout.Id;

        foreach (var ally in run.Allies)
            if (run.PilotOf(ally) is { } pilotId)
                PilotCrests[pilotId] = ally.Loadout;

        ActiveMission = run;
        _missionSave = saved;
        string where = run.Phase == MissionPhase.Rest
            ? $"resting after encounter {run.StageIndex + 1}"
            : $"at the start of encounter {run.StageIndex + 1} ({run.CurrentEncounter.Name})";
        warnings.Add($"Resumed {run.Mission.Name}, {where}.");
    }

    /// <summary>
    /// Gives every pilot in the content a Crest (their starting assembly) if the profile has none for them, and,
    /// for a new or pre-pilot profile, fills the lance in content order.
    /// </summary>
    private void EnsureRoster(bool fillLance)
    {
        foreach (var pilot in Catalog.Pilots.Values)
            if (!PilotCrests.ContainsKey(pilot.Id))
                PilotCrests[pilot.Id] = CrestLoadout.CreateFromConfiguration(pilot.Assembly, Catalog, pilot.Id);

        if (fillLance && Lance.Count == 0)
            Lance.AddRange(Catalog.Pilots.Keys.Take(MissionRun.MaxAllies));
    }

    private void CreateNewProfile()
    {
        var agile = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, Catalog, "asm1");
        var heavy = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, Catalog, "asm2");
        CrestAssemblies.Add(agile);
        CrestAssemblies.Add(heavy);
        SelectedCrestId = agile.Id;
        EnsureRoster(fillLance: true);

        // A few spare starter-build parts to experiment with; everything else comes from research.
        foreach (var id in new[]
                 {
                     "core_aether_dynamo", "core_aether_furnace", "mob_stride_legs", "mob_anchor_legs",
                     "eq_dragons_breath", "eq_shield_generator", "eq_repulsor", "eq_blink_drive",
                     "armor_heavy_plate", "eq_thermal_regulator", "eq_spell_amplifier", "util_cargo_rack",
                     "eq_talon", "eq_articulated_arm"
                 })
        {
            AddSpare(id);
        }
    }
}
