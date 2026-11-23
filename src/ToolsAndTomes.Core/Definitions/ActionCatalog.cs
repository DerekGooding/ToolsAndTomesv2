namespace ToolsAndTomes.Core.Definitions;

public static class ActionCatalog
{
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
                WindupBeats = 1,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 1,
                Range = 2,
                BaseDamage = 1,
                InterruptionPower = 2,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "weapon_vorpal",
                Name = "Vorpal Slash",
                Description = "Rapid melee blade striking twice per impact with no recovery.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.MeleeCommit,
                WindupBeats = 1,
                ImpactBeats = 1,
                RecoveryBeats = 0,
                CooldownBeats = 0,
                Range = 2,
                BaseDamage = 1,
                HitCount = 2,
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
                WindupBeats = 2,
                ImpactBeats = 1,
                RecoveryBeats = 2,
                CooldownBeats = 3,
                Range = 3,
                MoteCost = 1,
                BaseDamage = 1,
                HeavyBonusDamage = 2,
                HeavyWeightThreshold = 90,
                PushDistance = 2,
                InterruptionPower = 5,
                InterruptionResistance = 4
            },
            new()
            {
                Id = "weapon_talon",
                Name = "Talon Burst",
                Description = "Quick conventional ranged pistol shot with minimal warning.",
                Category = ActionCategory.Weapon,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.RangedQuick,
                WindupBeats = 1,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 2,
                Range = 8,
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
                WindupBeats = 2,
                ImpactBeats = 1,
                RecoveryBeats = 2,
                CooldownBeats = 4,
                Range = 10,
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
                WindupBeats = 1,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 3,
                Range = 4,
                AreaRadius = 2,
                AmmoCost = 1,
                HeatGenerated = 8,
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
                WindupBeats = 3,
                ImpactBeats = 1,
                RecoveryBeats = 2,
                CooldownBeats = 5,
                Range = 13,
                AreaRadius = 3,
                AmmoCost = 1,
                BaseDamage = 2,
                PushDistance = 2,
                InterruptionPower = 5,
                InterruptionResistance = 2
            },
            new()
            {
                Id = "defense_repulsor",
                Name = "Repulsor Pulse",
                Description = "Reaction: short-range blast dealing 1 damage and pushing the target back.",
                Category = ActionCategory.Defense,
                Targeting = TargetingType.EnemyUnit,
                Telegraph = TelegraphKind.DefensiveReaction,
                WindupBeats = 0,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 6,
                Range = 3,
                BaseDamage = 1,
                PushDistance = 4,
                InterruptionPower = 4,
                InterruptionResistance = 5,
                IsReactive = true
            },
            new()
            {
                Id = "defense_blink",
                Name = "Blink Drive",
                Description = "Reaction: teleport away, evading impacts that resolve this beat.",
                Category = ActionCategory.Defense,
                Targeting = TargetingType.Point,
                Telegraph = TelegraphKind.DefensiveReaction,
                WindupBeats = 0,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 10,
                Range = 5,
                BlinkDistance = 5,
                EvadeBeats = 1,
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
                WindupBeats = 1,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 4,
                Range = 8,
                WitReserve = 1,
                PushDistance = 4,
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
                WindupBeats = 1,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 4,
                Range = 9,
                WitReserve = 1,
                PushDistance = -4,
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
                WindupBeats = 3,
                ImpactBeats = 1,
                RecoveryBeats = 2,
                CooldownBeats = 8,
                Range = 9,
                WitReserve = 2,
                MoteCost = 1,
                BaseDamage = 1,
                HeavyBonusDamage = 2,
                HeavyWeightThreshold = 90,
                InterruptionPower = 5,
                InterruptionResistance = 3,
                RequiresSpellAmplifier = true
            },
            new()
            {
                Id = "spell_aegis",
                Name = "Aegis Barrier",
                Description = "Reaction: arcane ward absorbing the damage of the next 2 hits for 4 beats.",
                Category = ActionCategory.Spell,
                Targeting = TargetingType.Self,
                Telegraph = TelegraphKind.DefensiveReaction,
                WindupBeats = 0,
                ImpactBeats = 1,
                RecoveryBeats = 1,
                CooldownBeats = 12,
                Range = 0,
                WitReserve = 2,
                MoteCost = 1,
                AegisBlocks = 2,
                AegisDurationBeats = 4,
                InterruptionResistance = 4,
                RequiresSpellbook = true,
                IsReactive = true
            },
            new()
            {
                Id = "spell_square_summon",
                Name = "Square Summon",
                Description = "Condenses 4 Motes of Moment into a unit of fast-iron. Slow and easily broken.",
                Category = ActionCategory.Spell,
                Targeting = TargetingType.Self,
                Telegraph = TelegraphKind.SpellWindup,
                WindupBeats = 4,
                ImpactBeats = 1,
                RecoveryBeats = 2,
                CooldownBeats = 0,
                Range = 0,
                WitReserve = 1,
                MoteCost = 4,
                FastIronProduced = 1,
                InterruptionResistance = 1,
                RequiresSpellbook = true
            }
        };

        return list.ToDictionary(a => a.Id);
    }
}
