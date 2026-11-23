namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// Illustrative starter component assemblies. These are saved component lists (in Damage Order, Core last),
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
            "eq_jump_jets",
            "eq_talon",
            "eq_vorpal",
            "eq_spell_amplifier",
            "eq_blink_drive",
            "mob_stride_legs",
            "spellbook_grimoire",
            "core_aether_dynamo"
        ],
        ReactionRules =
        [
            new ReactionRule { ActionId = "defense_blink", MinIncomingDamage = 2 }
        ]
    };

    public static CrestConfiguration HeavyAnchor { get; } = new()
    {
        Id = "cfg_heavy_anchor",
        Name = "Heavy Anchor Assembly",
        Description = "Illustrative heavy component build using a Heavy Aether Furnace, armor plating, Shield Generator, Repulsor, Titan Hammer, and Dragon's Breath.",
        ComponentIds =
        [
            "armor_heavy_plate",
            "eq_thermal_regulator",
            "eq_dragons_breath",
            "eq_repulsor",
            "eq_shield_generator",
            "eq_titan",
            "mob_anchor_legs",
            "spellbook_grimoire",
            "core_aether_furnace"
        ],
        ReactionRules =
        [
            new ReactionRule { ActionId = "defense_repulsor", Telegraph = TelegraphKind.MeleeCommit },
            new ReactionRule { ActionId = "spell_aegis", MinIncomingDamage = 2 }
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
                ComponentIds = ["eq_jump_jets", "eq_talon", "mob_stride_legs", "core_aether_dynamo"]
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
                ComponentIds = ["armor_heavy_plate", "eq_righteous", "eq_titan", "mob_anchor_legs", "core_aether_furnace"]
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
                ComponentIds = ["eq_shield_generator", "mob_anchor_legs", "eq_artillery_mortar", "core_aether_dynamo"]
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
