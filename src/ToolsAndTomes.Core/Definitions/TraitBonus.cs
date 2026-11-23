namespace ToolsAndTomes.Core.Definitions;

/// <summary>Properties of a target's current state that damage types can exploit.</summary>
public enum TargetTrait
{
    /// <summary>Total installed weight at or above the bonus's threshold.</summary>
    Heavy,

    /// <summary>Has at least one currently operational armor component (regardless of Damage Order).</summary>
    Armored
}

/// <summary>Additional damage per hit against targets with a trait (e.g. Gravity vs Heavy, Acid vs Armored).</summary>
public sealed record TraitBonus(TargetTrait Trait, int BonusDamage, int WeightThreshold = 90);
