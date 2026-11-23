using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Definitions;

public sealed class GameCatalog
{
    /// <summary>The shipped content (embedded JSON under Content/), validated on first use.</summary>
    public static GameCatalog Default => LazyDefault.Value;

    private static readonly Lazy<GameCatalog> LazyDefault = new(Content.ContentSerializer.LoadEmbedded);

    public IReadOnlyDictionary<string, ActionDefinition> Actions { get; }
    public IReadOnlyDictionary<string, EquipmentDefinition> Equipment { get; }
    public IReadOnlyDictionary<string, CrestConfiguration> StarterConfigurations { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }
    public RulesConfig Rules { get; }

    public GameCatalog(
        IReadOnlyDictionary<string, ActionDefinition> actions,
        IReadOnlyDictionary<string, EquipmentDefinition> equipment,
        IReadOnlyDictionary<string, CrestConfiguration> starterConfigurations,
        IReadOnlyDictionary<string, EnemyDefinition> enemies,
        RulesConfig? rules = null)
    {
        Rules = rules ?? RulesConfig.Default;
        Actions = actions;
        Equipment = equipment;
        StarterConfigurations = starterConfigurations;
        Enemies = enemies;
    }

    public ActionDefinition GetAction(string id) => Actions[id];
    public EquipmentDefinition GetEquipment(string id) => Equipment[id];
}
