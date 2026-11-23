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
    public static int DamagePerHit(ActionDefinition action, Combatant target) =>
        action.BaseDamage + action.TraitBonuses.Where(b => HasTrait(target, b)).Sum(b => b.BonusDamage);
}
