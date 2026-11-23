using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.App;

public sealed class AppSessionState
{
    private int _nextInstanceCounter = 100;
    private int _nextAssemblyCounter = 1;

    public GameCatalog Catalog { get; } = GameCatalog.Default;
    public List<CrestLoadout> CrestAssemblies { get; } = [];
    public List<CrestConfiguration> SavedBlueprints { get; } = [];
    public List<EquipmentInstance> SpareInventory { get; } = [];
    public string SelectedCrestId { get; set; }

    public CombatSimulation? ActiveSimulation { get; private set; }
    public EnemyAIController ActiveAI { get; } = new(decisionIntervalTicks: 8);
    public int EncounterSeed { get; set; } = 1337;

    public CrestLoadout SelectedLoadout =>
        CrestAssemblies.First(c => c.Id == SelectedCrestId);

    public AppSessionState()
    {
        var agile = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, Catalog, "asm1");
        var heavy = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.HeavyAnchor, Catalog, "asm2");

        CrestAssemblies.Add(agile);
        CrestAssemblies.Add(heavy);
        SavedBlueprints.Add(agile.ExportConfiguration());
        SavedBlueprints.Add(heavy.ExportConfiguration());
        SelectedCrestId = agile.Id;

        AddSpare("core_aether_dynamo");
        AddSpare("core_aether_furnace");
        AddSpare("mob_stride_legs");
        AddSpare("mob_anchor_legs");
        AddSpare("eq_righteous");
        AddSpare("eq_falconet");
        AddSpare("eq_dragons_breath");
        AddSpare("eq_shield_generator");
        AddSpare("eq_repulsor");
        AddSpare("eq_blink_drive");
        AddSpare("armor_heavy_plate");
        AddSpare("eq_thermal_regulator");
        AddSpare("eq_spell_amplifier");
    }

    public EquipmentInstance CreateNewInstance(string equipmentDefId)
    {
        var def = Catalog.GetEquipment(equipmentDefId);
        return new EquipmentInstance($"inv_{_nextInstanceCounter++}_{def.Id}", def);
    }

    public void AddSpare(string equipmentDefId) => SpareInventory.Add(CreateNewInstance(equipmentDefId));

    public CrestLoadout SaveCurrentAsNewAssembly(string name)
    {
        string id = $"custom_asm_{_nextAssemblyCounter++}";
        var blueprint = SelectedLoadout.ExportConfiguration(id, $"Saved from {SelectedLoadout.Name}");
        blueprint = blueprint with { Name = string.IsNullOrWhiteSpace(name) ? $"Custom Assembly {_nextAssemblyCounter}" : name.Trim() };
        SavedBlueprints.Add(blueprint);

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

    public CombatSimulation StartEncounter()
    {
        ActiveSimulation = EncounterFactory.CreateTestEncounter(SelectedLoadout, EncounterSeed, Catalog);
        ActiveSimulation.Pause();
        return ActiveSimulation;
    }
}
