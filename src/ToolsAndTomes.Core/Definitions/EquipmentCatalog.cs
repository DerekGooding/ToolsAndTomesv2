namespace ToolsAndTomes.Core.Definitions;

public static class EquipmentCatalog
{
    public static IReadOnlyDictionary<string, EquipmentDefinition> All { get; } = Build();

    private static Dictionary<string, EquipmentDefinition> Build()
    {
        var list = new EquipmentDefinition[]
        {
            new()
            {
                Id = "core_strider",
                Name = "Strider Core",
                Description = "Lightweight arcane-mechanical reactor. If disabled, the Crest falls.",
                SlotType = EquipmentSlotType.Core,
                MaxDurability = 3,
                Weight = 20,
                IsCore = true
            },
            new()
            {
                Id = "core_bulwark",
                Name = "Bulwark Core",
                Description = "Reinforced heavy reactor built to endure prolonged engagements.",
                SlotType = EquipmentSlotType.Core,
                MaxDurability = 4,
                Weight = 40,
                DisplacementResistance = 0.25,
                IsCore = true
            },
            new()
            {
                Id = "armor_heavy_plate",
                Name = "Heavy Armor Plating",
                Description = "Adds +1 protection d6 and resists displacement at the cost of weight.",
                SlotType = EquipmentSlotType.Armor,
                MaxDurability = 3,
                Weight = 35,
                BonusProtectionDice = 1,
                DisplacementResistance = 0.40,
                IsArmor = true
            },
            new()
            {
                Id = "spellbook_grimoire",
                Name = "Inventor's Spellbook",
                Description = "Raises Wit capacity to 10, adds +2 Mote storage, and unlocks arcane spells.",
                SlotType = EquipmentSlotType.Spellbook,
                MaxDurability = 2,
                Weight = 2,
                BonusMoteStorage = 2,
                IsSpellbook = true,
                GrantedActionIds = ["spell_push", "spell_pull", "spell_aegis", "spell_gravity_crush"]
            },
            new()
            {
                Id = "eq_righteous",
                Name = "Righteous Blade",
                Description = "Reliable short-commitment melee blade.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 10,
                GrantedActionIds = ["weapon_righteous"]
            },
            new()
            {
                Id = "eq_vorpal",
                Name = "Vorpal Edge",
                Description = "Ultra-fast melee blade enabling rapid combinations.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 6,
                GrantedActionIds = ["weapon_vorpal"]
            },
            new()
            {
                Id = "eq_titan",
                Name = "Titan Hammer",
                Description = "Mote-driven heavy melee weapon that punishes heavy Crests.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 3,
                WitTax = 2,
                Weight = 24,
                GrantedActionIds = ["weapon_titan"]
            },
            new()
            {
                Id = "eq_talon",
                Name = "Talon Machine Pistol",
                Description = "Quick conventional sidearm with low telegraphing.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 8,
                GrantedActionIds = ["weapon_talon"]
            },
            new()
            {
                Id = "eq_falconet",
                Name = "Falconet Missile Pod",
                Description = "Medium-range missile rack (6 ammo) firing committed 3-shot salvos.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 2,
                WitTax = 2,
                Weight = 18,
                MaxAmmo = 6,
                GrantedActionIds = ["weapon_falconet"]
            },
            new()
            {
                Id = "eq_dragons_breath",
                Name = "Dragon's Breath Projector",
                Description = "Short-range thermal weapon (4 ammo) that generates Crest heat.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 14,
                MaxAmmo = 4,
                GrantedActionIds = ["weapon_dragons_breath"]
            },
            new()
            {
                Id = "eq_artillery_mortar",
                Name = "Siege Mortar",
                Description = "Long-range artillery tube firing telegraphed high-impact shells.",
                SlotType = EquipmentSlotType.Weapon,
                MaxDurability = 2,
                WitTax = 2,
                Weight = 22,
                MaxAmmo = 6,
                GrantedActionIds = ["weapon_artillery_mortar"]
            },
            new()
            {
                Id = "eq_shield_generator",
                Name = "Shield Generator",
                Description = "Passive defensive field granting +1 protection d6 while operational.",
                SlotType = EquipmentSlotType.Defense,
                MaxDurability = 2,
                WitTax = 2,
                Weight = 10,
                BonusProtectionDice = 1
            },
            new()
            {
                Id = "eq_repulsor",
                Name = "Repulsor Emitter",
                Description = "Reactive short-range pulse dealing 1 damage and shoving attackers away.",
                SlotType = EquipmentSlotType.Defense,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 8,
                GrantedActionIds = ["defense_repulsor"]
            },
            new()
            {
                Id = "eq_blink_drive",
                Name = "Blink Drive",
                Description = "Reactive spatial shifter enabling evasion of committed attacks.",
                SlotType = EquipmentSlotType.Mobility,
                MaxDurability = 2,
                WitTax = 2,
                Weight = 6,
                GrantedActionIds = ["defense_blink"]
            },
            new()
            {
                Id = "eq_jump_jets",
                Name = "Jump Jets",
                Description = "Boosts Crest movement speed (+2.0 m/s) while operational.",
                SlotType = EquipmentSlotType.Mobility,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 6,
                MoveSpeedBonus = 2.0
            },
            new()
            {
                Id = "eq_thermal_regulator",
                Name = "Thermal Regulator",
                Description = "Vents excess heat (+2.5/s). If disabled, thermal weapons risk overheating.",
                SlotType = EquipmentSlotType.Utility,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 8,
                HeatDissipationPerSecond = 2.5
            },
            new()
            {
                Id = "eq_spell_amplifier",
                Name = "Arcane Amplifier",
                Description = "Channels heavy pilot spells such as Gravity Crush through the Crest.",
                SlotType = EquipmentSlotType.Utility,
                MaxDurability = 2,
                WitTax = 1,
                Weight = 6,
                IsSpellAmplifier = true
            }
        };

        return list.ToDictionary(e => e.Id);
    }
}
