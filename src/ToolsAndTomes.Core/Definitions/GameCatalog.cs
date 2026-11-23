using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Definitions;

public sealed class GameCatalog
{
    /// <summary>The shipped content (embedded JSON under Content/), validated on first use.</summary>
    public static GameCatalog Default => LazyDefault.Value;

    private static readonly Lazy<GameCatalog> LazyDefault = new(Content.ContentSerializer.LoadEmbedded);

    private static readonly IReadOnlyDictionary<string, ExoticMaterialDefinition> NoMaterials =
        new Dictionary<string, ExoticMaterialDefinition>();
    private static readonly IReadOnlyDictionary<string, EncounterDefinition> NoEncounters =
        new Dictionary<string, EncounterDefinition>();
    private static readonly IReadOnlyDictionary<string, ConsumableDefinition> NoConsumables =
        new Dictionary<string, ConsumableDefinition>();

    public IReadOnlyDictionary<string, ActionDefinition> Actions { get; }
    public IReadOnlyDictionary<string, EquipmentDefinition> Equipment { get; }
    public IReadOnlyDictionary<string, CrestConfiguration> StarterConfigurations { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }
    public IReadOnlyDictionary<string, ExoticMaterialDefinition> Materials { get; }
    public IReadOnlyDictionary<string, EncounterDefinition> Encounters { get; }
    public IReadOnlyDictionary<string, ConsumableDefinition> Consumables { get; }
    public RulesConfig Rules { get; }

    /// <summary>
    /// Components known from the start: everything that appears in a starter build.
    /// All others must be unlocked by researching an exotic material.
    /// </summary>
    public IReadOnlySet<string> BaselineComponentIds { get; }

    public GameCatalog(
        IReadOnlyDictionary<string, ActionDefinition> actions,
        IReadOnlyDictionary<string, EquipmentDefinition> equipment,
        IReadOnlyDictionary<string, CrestConfiguration> starterConfigurations,
        IReadOnlyDictionary<string, EnemyDefinition> enemies,
        RulesConfig? rules = null,
        IReadOnlyDictionary<string, ExoticMaterialDefinition>? materials = null,
        IReadOnlyDictionary<string, EncounterDefinition>? encounters = null,
        IReadOnlyDictionary<string, ConsumableDefinition>? consumables = null)
    {
        Rules = rules ?? RulesConfig.Default;
        Actions = actions;
        Equipment = equipment;
        StarterConfigurations = starterConfigurations;
        Enemies = enemies;
        Materials = materials ?? NoMaterials;
        Encounters = encounters ?? NoEncounters;
        Consumables = consumables ?? NoConsumables;
        BaselineComponentIds = starterConfigurations.Values.SelectMany(c => c.ComponentIds).ToHashSet();
    }

    /// <summary>A copy of this catalog with different global rules.</summary>
    public GameCatalog WithRules(RulesConfig rules) =>
        new(Actions, Equipment, StarterConfigurations, Enemies, rules, Materials, Encounters, Consumables);

    public ActionDefinition GetAction(string id) => Actions[id];
    public EquipmentDefinition GetEquipment(string id) => Equipment[id];
}
