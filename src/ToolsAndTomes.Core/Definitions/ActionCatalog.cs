namespace ToolsAndTomes.Core.Definitions;

public static class ActionCatalog
{
    public const int TicksPerSecond = 20;

    public static IReadOnlyDictionary<string, ActionDefinition> All { get; } = Build();

    private static Dictionary<string, ActionDefinition> Build()
    {
        var list = new ActionDefinition[]
        {
            new()
            {
                Id = "weapon_righteous",
                Name = "Righteous Strike",
                Description = "Balanced melee strike with short commitment.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.MeleeCommit,
                WindupTicks = 6,
                ExecutionTicks = 2,
                RecoveryTicks = 6,
                CooldownTicks = 6,
                Range = 2.2,
                BaseDamage = 1,
                InterruptionPower = 2,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "weapon_vorpal",
                Name = "Vorpal Slash",
                Description = "Rapid melee blade capable of two strikes per second.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.MeleeCommit,
                WindupTicks = 3,
                ExecutionTicks = 2,
                RecoveryTicks = 3,
                CooldownTicks = 2,
                Range = 2.0,
                BaseDamage = 1,
                InterruptionPower = 1,
                InterruptionResistance = 1
            },
            new()
            {
                Id = "weapon_titan",
                Name = "Titan Cleave",
                Description = "Heavy Mote-powered melee blow. Deals bonus damage to heavy Crests.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.MeleeCommit,
                WindupTicks = 16,
                ExecutionTicks = 4,
                RecoveryTicks = 12,
                CooldownTicks = 16,
                Range = 2.5,
                MoteCost = 1,
                BaseDamage = 1,
                HeavyBonusDamage = 2,
                HeavyWeightThreshold = 90.0,
                PushDistance = 1.5,
                InterruptionPower = 5,
                InterruptionResistance = 4
            },
            new()
            {
                Id = "weapon_talon",
                Name = "Talon Burst",
                Description = "Immediate conventional ranged pistol shot with minimal warning.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.RangedQuick,
                WindupTicks = 3,
                ExecutionTicks = 2,
                RecoveryTicks = 5,
                CooldownTicks = 8,
                Range = 8.5,
                BaseDamage = 1,
                InterruptionPower = 1,
                InterruptionResistance = 1
            },
            new()
            {
                Id = "weapon_falconet",
                Name = "Falconet Salvo",
                Description = "Committed 3-missile volley consuming 3 ammo.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.AreaBlast,
                WindupTicks = 14,
                ExecutionTicks = 6,
                RecoveryTicks = 10,
                CooldownTicks = 24,
                Range = 10.5,
                AmmoCost = 3,
                BaseDamage = 1,
                HitCount = 3,
                InterruptionPower = 3,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "weapon_dragons_breath",
                Name = "Dragon's Breath",
                Description = "Short-range thermal blast. Consumes ammo and builds Crest heat.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.AreaBlast,
                WindupTicks = 8,
                ExecutionTicks = 4,
                RecoveryTicks = 8,
                CooldownTicks = 14,
                Range = 4.5,
                AreaRadius = 1.8,
                AmmoCost = 1,
                HeatGenerated = 5.5,
                BaseDamage = 1,
                HitCount = 2,
                InterruptionPower = 3,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "weapon_artillery_mortar",
                Name = "Siege Mortar",
                Description = "Heavily telegraphed high-impact area bombardment.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.Point,
                Telegraph = TelegraphKind.AreaBlast,
                WindupTicks = 32,
                ExecutionTicks = 4,
                RecoveryTicks = 18,
                CooldownTicks = 30,
                Range = 13.0,
                AreaRadius = 2.6,
                AmmoCost = 1,
                BaseDamage = 2,
                PushDistance = 2.0,
                InterruptionPower = 5,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "defense_repulsor",
                Name = "Repulsor Pulse",
                Description = "Reactive short-range blast dealing 1 damage and pushing enemies back.",
                Category = ActionCategory.Defense,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.DefensiveReaction,
                WindupTicks = 2,
                ExecutionTicks = 2,
                RecoveryTicks = 6,
                CooldownTicks = 36,
                Range = 3.2,
                BaseDamage = 1,
                PushDistance = 3.8,
                InterruptionPower = 4,
                InterruptionResistance = 5,
                IsReactive = true
            },
            new()
            {
                Id = "defense_blink",
                Name = "Blink Drive",
                Description = "Reactive teleportation that evades incoming attacks during its window.",
                Category = ActionCategory.Defense,
                Targeting = TargetingType.Point,
                Telegraph = TelegraphKind.DefensiveReaction,
                WindupTicks = 1,
                ExecutionTicks = 2,
                RecoveryTicks = 4,
                CooldownTicks = 50,
                Range = 5.5,
                BlinkDistance = 5.5,
                EvadeDurationTicks = 12,
                WitReserve = 1,
                InterruptionResistance = 6,
                IsReactive = true
            },
            new()
            {
                Id = "spell_push",
                Name = "Kinetic Push",
                Description = "Low-windup telekinetic shove that displaces and disrupts targets.",
                Category = ActionCategory.Spell,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.SpellWindup,
                WindupTicks = 5,
                ExecutionTicks = 2,
                RecoveryTicks = 6,
                CooldownTicks = 20,
                Range = 8.0,
                WitReserve = 1,
                PushDistance = 3.5,
                InterruptionPower = 3,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "spell_pull",
                Name = "Kinetic Pull",
                Description = "Low-windup telekinetic yank pulling targets closer and disrupting windups.",
                Category = ActionCategory.Spell,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.SpellWindup,
                WindupTicks = 5,
                ExecutionTicks = 2,
                RecoveryTicks = 6,
                CooldownTicks = 20,
                Range = 9.0,
                WitReserve = 1,
                PushDistance = -3.5,
                InterruptionPower = 3,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "spell_gravity_crush",
                Name = "Gravity Crush",
                Description = "Committed gravity well crushing heavy Crests and canceling actions.",
                Category = ActionCategory.Spell,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.SpellWindup,
                WindupTicks = 18,
                ExecutionTicks = 4,
                RecoveryTicks = 10,
                CooldownTicks = 40,
                Range = 9.0,
                WitReserve = 2,
                MoteCost = 1,
                BaseDamage = 1,
                HeavyBonusDamage = 2,
                HeavyWeightThreshold = 90.0,
                InterruptionPower = 5,
                InterruptionResistance = 3,
                RequiresSpellAmplifier = true
            },
            new()
            {
                Id = "spell_aegis",
                Name = "Aegis Barrier",
                Description = "Active arcane ward absorbing the next 2 incoming hits while sustained.",
                Category = ActionCategory.Spell,
                Targeting = TargetingType.Self,
                Telegraph = TelegraphKind.DefensiveReaction,
                WindupTicks = 4,
                ExecutionTicks = 2,
                RecoveryTicks = 4,
                CooldownTicks = 60,
                Range = 0,
                WitReserve = 2,
                MoteCost = 1,
                AegisBlocks = 2,
                AegisDurationTicks = 80,
                InterruptionResistance = 4,
                RequiresSpellbook = true,
                IsReactive = true
            }
        };

        return list.ToDictionary(a => a.Id);
    }
}
