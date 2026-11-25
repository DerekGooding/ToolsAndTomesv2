using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

public static class TraitRules
{
    /// <summary>Traits are checked against the target's current state when the impact resolves.</summary>
    public static bool HasTrait(Combatant target, TraitBonus bonus) => bonus.Trait switch
    {
        TargetTrait.Heavy => target.Stats.TotalWeight >= bonus.WeightThreshold,
        TargetTrait.Armored => target.Loadout.Installed.Any(e => e.IsOperational && e.Definition.IsArmor),
        _ => false
    };

    /// <summary>Damage per hit of <paramref name="action"/> against <paramref name="target"/>, including trait bonuses.</summary>
    /// <summary>Damage per hit: base, plus trait bonuses, plus the damage type's bonus (only on hits that deal damage).</summary>
    public static int DamagePerHit(ActionDefinition action, Combatant target)
    {
        int damage = action.BaseDamage + action.TraitBonuses.Where(b => HasTrait(target, b)).Sum(b => b.BonusDamage);
        return damage > 0 ? damage + DamageTypeBonus(action.DamageType, target) : damage;
    }

    /// <summary>
    /// Electrical: +1 per tome the target holds (max +2). Disjunction: +1 per 3 points of the target's equipment
    /// Wit tax. (Amounts from <see cref="RulesConfig"/>.)
    /// </summary>
    public static int DamageTypeBonus(DamageType type, Combatant target)
    {
        var r = target.Rules;
        return type switch
        {
            DamageType.Electrical => Math.Min(r.ElectricalMaxBonus, target.Stats.TomesHeld * r.ElectricalBonusPerTome),
            DamageType.Disjunction => r.DisjunctionWitTaxPerBonus <= 0 ? 0 : target.Stats.EquipmentWitTax / r.DisjunctionWitTaxPerBonus,
            _ => 0
        };
    }
}
