namespace ToolsAndTomes.Core.Definitions;

public sealed class GameCatalog
{
    public static GameCatalog Default { get; } = new(
        ActionCatalog.All,
        EquipmentCatalog.All,
        ConfigurationCatalog.StarterConfigurations,
        ConfigurationCatalog.Enemies);

    public IReadOnlyDictionary<string, ActionDefinition> Actions { get; }
    public IReadOnlyDictionary<string, EquipmentDefinition> Equipment { get; }
    public IReadOnlyDictionary<string, CrestConfiguration> StarterConfigurations { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }

    public GameCatalog(
        IReadOnlyDictionary<string, ActionDefinition> actions,
        IReadOnlyDictionary<string, EquipmentDefinition> equipment,
        IReadOnlyDictionary<string, CrestConfiguration> starterConfigurations,
        IReadOnlyDictionary<string, EnemyDefinition> enemies)
    {
        Actions = actions;
        Equipment = equipment;
        StarterConfigurations = starterConfigurations;
        Enemies = enemies;
    }

    public ActionDefinition GetAction(string id) => Actions[id];
    public EquipmentDefinition GetEquipment(string id) => Equipment[id];
}
