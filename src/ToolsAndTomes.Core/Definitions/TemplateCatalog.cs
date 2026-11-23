namespace ToolsAndTomes.Core.Definitions;

public static class TemplateCatalog
{
    public static CrestTemplateDefinition Strider { get; } = new()
    {
        Id = "crest_strider",
        Name = "Strider",
        Description = "Lightweight, agile chassis built for evasion, repositioning, and fast strikes.",
        BaseMoveSpeed = 4.6,
        BaseWeight = 30,
        BaseDisplacementResistance = 0.05,
        BaseHeatCapacity = 9.0,
        BaseHeatDissipationPerSecond = 1.0,
        DefaultEquipmentIds =
        [
            "core_strider",
            "spellbook_grimoire",
            "eq_vorpal",
            "eq_talon",
            "eq_blink_drive",
            "eq_jump_jets",
            "eq_spell_amplifier"
        ]
    };

    public static CrestTemplateDefinition Bulwark { get; } = new()
    {
        Id = "crest_bulwark",
        Name = "Bulwark",
        Description = "Heavy armored chassis built to anchor the line, absorb fire, and land heavy blows.",
        BaseMoveSpeed = 3.0,
        BaseWeight = 55,
        BaseDisplacementResistance = 0.25,
        BaseHeatCapacity = 12.0,
        BaseHeatDissipationPerSecond = 1.0,
        DefaultEquipmentIds =
        [
            "core_bulwark",
            "armor_heavy_plate",
            "spellbook_grimoire",
            "eq_titan",
            "eq_dragons_breath",
            "eq_shield_generator",
            "eq_repulsor",
            "eq_thermal_regulator"
        ]
    };

    public static IReadOnlyDictionary<string, CrestTemplateDefinition> PlayerCrests { get; } =
        new Dictionary<string, CrestTemplateDefinition>
        {
            [Strider.Id] = Strider,
            [Bulwark.Id] = Bulwark
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
            Template = new CrestTemplateDefinition
            {
                Id = "tpl_skirmisher",
                Name = "Skirmisher Chassis",
                Description = "Mobile ranged harassment unit.",
                BaseMoveSpeed = 4.2,
                BaseWeight = 30,
                BaseDisplacementResistance = 0.05,
                DefaultEquipmentIds = ["core_strider", "eq_talon", "eq_jump_jets"]
            }
        };

        var bruiser = new EnemyDefinition
        {
            Id = "enemy_bruiser",
            Name = "Ironclad Bruiser",
            Archetype = EnemyArchetype.Bruiser,
            PreferredRange = 2.2,
            Template = new CrestTemplateDefinition
            {
                Id = "tpl_bruiser",
                Name = "Bruiser Chassis",
                Description = "Heavy melee combatant using committed strikes.",
                BaseMoveSpeed = 2.8,
                BaseWeight = 55,
                BaseDisplacementResistance = 0.20,
                DefaultEquipmentIds = ["core_bulwark", "armor_heavy_plate", "eq_titan", "eq_righteous"]
            }
        };

        var artillery = new EnemyDefinition
        {
            Id = "enemy_artillery",
            Name = "Bastion Artillery",
            Archetype = EnemyArchetype.Artillery,
            PreferredRange = 11.0,
            Template = new CrestTemplateDefinition
            {
                Id = "tpl_artillery",
                Name = "Artillery Chassis",
                Description = "Long-range fire support using heavily telegraphed mortar strikes.",
                BaseMoveSpeed = 2.4,
                BaseWeight = 45,
                BaseDisplacementResistance = 0.15,
                DefaultEquipmentIds = ["core_strider", "eq_artillery_mortar", "eq_shield_generator"]
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
