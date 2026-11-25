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
    private static readonly IReadOnlyDictionary<string, MissionDefinition> NoMissions =
        new Dictionary<string, MissionDefinition>();
    private static readonly IReadOnlyDictionary<string, PilotDefinition> NoPilots =
        new Dictionary<string, PilotDefinition>();
    private static readonly IReadOnlyDictionary<string, ObstacleDefinition> NoObstacles =
        new Dictionary<string, ObstacleDefinition>();

    public IReadOnlyDictionary<string, ActionDefinition> Actions { get; }
    public IReadOnlyDictionary<string, EquipmentDefinition> Equipment { get; }
    public IReadOnlyDictionary<string, CrestConfiguration> StarterConfigurations { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }
    public IReadOnlyDictionary<string, ExoticMaterialDefinition> Materials { get; }
    public IReadOnlyDictionary<string, EncounterDefinition> Encounters { get; }
    public IReadOnlyDictionary<string, ConsumableDefinition> Consumables { get; }
    public IReadOnlyDictionary<string, MissionDefinition> Missions { get; }

    /// <summary>Named allied pilots (and their starting Crests) who can join the player's lance.</summary>
    public IReadOnlyDictionary<string, PilotDefinition> Pilots { get; }

    /// <summary>Kinds of obstacle that encounters place and spells raise.</summary>
    public IReadOnlyDictionary<string, ObstacleDefinition> Obstacles { get; }

    /// <summary>What wrecks become when the destroying hit leaves rubble.</summary>
    public ObstacleDefinition? Rubble => Obstacles.Values.FirstOrDefault(o => o.IsRubble);
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
        IReadOnlyDictionary<string, ConsumableDefinition>? consumables = null,
        IReadOnlyDictionary<string, MissionDefinition>? missions = null,
        IReadOnlyDictionary<string, PilotDefinition>? pilots = null,
        IReadOnlyDictionary<string, ObstacleDefinition>? obstacles = null)
    {
        Rules = rules ?? RulesConfig.Default;
        Actions = actions;
        Equipment = equipment;
        StarterConfigurations = starterConfigurations;
        Enemies = enemies;
        Materials = materials ?? NoMaterials;
        Encounters = encounters ?? NoEncounters;
        Consumables = consumables ?? NoConsumables;
        Missions = missions ?? NoMissions;
        Pilots = pilots ?? NoPilots;
        Obstacles = obstacles ?? NoObstacles;
        BaselineComponentIds = starterConfigurations.Values.SelectMany(c => c.ComponentIds).ToHashSet();
    }

    /// <summary>A copy of this catalog with different global rules.</summary>
    public GameCatalog WithRules(RulesConfig rules) =>
        new(Actions, Equipment, StarterConfigurations, Enemies, rules, Materials, Encounters, Consumables, Missions, Pilots, Obstacles);

    public ActionDefinition GetAction(string id) => Actions[id];
    public EquipmentDefinition GetEquipment(string id) => Equipment[id];
}
