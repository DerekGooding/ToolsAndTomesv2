using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.App;

public sealed class AppSessionState
{
    private int _nextInstanceCounter = 100;

    public GameCatalog Catalog { get; } = GameCatalog.Default;
    public Dictionary<string, CrestLoadout> PlayerCrests { get; }
    public List<EquipmentInstance> SpareInventory { get; } = [];
    public string SelectedCrestId { get; set; } = TemplateCatalog.Strider.Id;

    public CombatSimulation? ActiveSimulation { get; private set; }
    public EnemyAIController ActiveAI { get; } = new(decisionIntervalTicks: 8);
    public int EncounterSeed { get; set; } = 1337;

    public CrestLoadout SelectedLoadout => PlayerCrests[SelectedCrestId];

    public AppSessionState()
    {
        PlayerCrests = new Dictionary<string, CrestLoadout>
        {
            [TemplateCatalog.Strider.Id] = CrestLoadout.CreateFromTemplate(TemplateCatalog.Strider, Catalog, "strider"),
            [TemplateCatalog.Bulwark.Id] = CrestLoadout.CreateFromTemplate(TemplateCatalog.Bulwark, Catalog, "bulwark")
        };

        // Populate initial spare parts so the player can immediately experiment
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

    public void AddSpare(string equipmentDefId)
    {
        SpareInventory.Add(CreateNewInstance(equipmentDefId));
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

    public CombatSimulation StartEncounter()
    {
        ActiveSimulation = EncounterFactory.CreateTestEncounter(SelectedLoadout, EncounterSeed, Catalog);
        ActiveSimulation.Pause(); // Start paused so player can inspect the battlefield and plan!
        return ActiveSimulation;
    }
}
