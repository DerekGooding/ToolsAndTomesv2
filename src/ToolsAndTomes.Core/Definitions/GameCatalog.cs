namespace ToolsAndTomes.Core.Definitions;

public sealed class GameCatalog
{
    public static GameCatalog Default { get; } = new(
        ActionCatalog.All,
        EquipmentCatalog.All,
        TemplateCatalog.PlayerCrests,
        TemplateCatalog.Enemies);

    public IReadOnlyDictionary<string, ActionDefinition> Actions { get; }
    public IReadOnlyDictionary<string, EquipmentDefinition> Equipment { get; }
    public IReadOnlyDictionary<string, CrestTemplateDefinition> PlayerCrests { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }

    public GameCatalog(
        IReadOnlyDictionary<string, ActionDefinition> actions,
        IReadOnlyDictionary<string, EquipmentDefinition> equipment,
        IReadOnlyDictionary<string, CrestTemplateDefinition> playerCrests,
        IReadOnlyDictionary<string, EnemyDefinition> enemies)
    {
        Actions = actions;
        Equipment = equipment;
        PlayerCrests = playerCrests;
        Enemies = enemies;
    }

    public ActionDefinition GetAction(string id) => Actions[id];
    public EquipmentDefinition GetEquipment(string id) => Equipment[id];
}
