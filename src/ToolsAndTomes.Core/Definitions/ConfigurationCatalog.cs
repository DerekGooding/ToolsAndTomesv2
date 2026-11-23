namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// Illustrative starter component assemblies. These are saved component lists,
/// not predefined Crest classes or chassis types.
/// </summary>
public static class ConfigurationCatalog
{
    public static CrestConfiguration AgileSkirmish { get; } = new()
    {
        Id = "cfg_agile_skirmish",
        Name = "Agile Skirmish Assembly",
        Description = "Illustrative light component build emphasizing Blink evasion, high speed, Vorpal/Talon strikes, and amplified spells.",
        ComponentIds =
        [
            "core_aether_dynamo",
            "mob_stride_legs",
            "spellbook_grimoire",
            "eq_vorpal",
            "eq_talon",
            "eq_blink_drive",
            "eq_jump_jets",
            "eq_spell_amplifier"
        ]
    };

    public static CrestConfiguration HeavyAnchor { get; } = new()
    {
        Id = "cfg_heavy_anchor",
        Name = "Heavy Anchor Assembly",
        Description = "Illustrative heavy component build using a Heavy Aether Furnace, armor plating, Shield Generator, Repulsor, Titan Hammer, and Dragon's Breath.",
        ComponentIds =
        [
            "core_aether_furnace",
            "mob_anchor_legs",
            "armor_heavy_plate",
            "spellbook_grimoire",
            "eq_titan",
            "eq_dragons_breath",
            "eq_shield_generator",
            "eq_repulsor",
            "eq_thermal_regulator"
        ]
    };

    public static IReadOnlyDictionary<string, CrestConfiguration> StarterConfigurations { get; } =
        new Dictionary<string, CrestConfiguration>
        {
            [AgileSkirmish.Id] = AgileSkirmish,
            [HeavyAnchor.Id] = HeavyAnchor
        };

    public static IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; } = BuildEnemies();

    private static Dictionary<string, EnemyDefinition> BuildEnemies()
    {
        var skirmisher = new EnemyDefinition
        {
            Id = "enemy_skirmisher",
            Name = "Vesper Skirmisher",
            Archetype = EnemyArchetype.Skirmisher,
            PreferredRange = 7.0,
            Assembly = new CrestConfiguration
            {
                Id = "cfg_enemy_skirmisher",
                Name = "Vesper Skirmisher",
                ComponentIds = ["core_aether_dynamo", "mob_stride_legs", "eq_talon", "eq_jump_jets"]
            }
        };

        var bruiser = new EnemyDefinition
        {
            Id = "enemy_bruiser",
            Name = "Ironclad Bruiser",
            Archetype = EnemyArchetype.Bruiser,
            PreferredRange = 2.2,
            Assembly = new CrestConfiguration
            {
                Id = "cfg_enemy_bruiser",
                Name = "Ironclad Bruiser",
                ComponentIds = ["core_aether_furnace", "mob_anchor_legs", "armor_heavy_plate", "eq_titan", "eq_righteous"]
            }
        };

        var artillery = new EnemyDefinition
        {
            Id = "enemy_artillery",
            Name = "Bastion Artillery",
            Archetype = EnemyArchetype.Artillery,
            PreferredRange = 11.0,
            Assembly = new CrestConfiguration
            {
                Id = "cfg_enemy_artillery",
                Name = "Bastion Artillery",
                ComponentIds = ["core_aether_dynamo", "mob_anchor_legs", "eq_artillery_mortar", "eq_shield_generator"]
            }
        };

        return new Dictionary<string, EnemyDefinition>
        {
            [skirmisher.Id] = skirmisher,
            [bruiser.Id] = bruiser,
            [artillery.Id] = artillery
        };
    }
}
