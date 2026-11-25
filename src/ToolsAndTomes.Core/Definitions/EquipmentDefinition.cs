namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// Data definition of a component. All quantities are small integers so they can be tracked at a table.
/// </summary>
public sealed record EquipmentDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ComponentCategory Category { get; init; }

    public int MaxDurability { get; init; } = 1;
    public int WitTax { get; init; }
    public int BonusWitCapacity { get; init; }
    public int Weight { get; init; }

    public int BonusProtectionDice { get; init; }

    /// <summary>Tabletop units subtracted from any push or pull applied to the Crest.</summary>
    public int DisplacementResistance { get; init; }

    /// <summary>Tabletop units of movement per beat contributed by this component.</summary>
    public int MoveSpeedBonus { get; init; }

    public int HeatCapacityContribution { get; init; }
    public int HeatDissipationPerBeat { get; init; }

    public int BonusMoteStorage { get; init; }

    /// <summary>Beats removed from the Mote generation interval.</summary>
    public int MoteIntervalReductionBeats { get; init; }

    /// <summary>Legs or other locomotion: required to walk. Walking occupies this component.</summary>
    public bool IsLocomotion { get; init; }

    /// <summary>Weight this locomotion component carries before movement slows.</summary>
    public int LoadCapacity { get; init; }

    /// <summary>Wit committed while this locomotion component is walking (0 = free movement).</summary>
    public int MovementWitCost { get; init; }

    public bool IsCore { get; init; }
    public bool IsArmor { get; init; }
    /// <summary>A tome: holding one (operational, in a hand) raises the pilot's base Wit; it grants its own spells.</summary>
    public bool IsTome { get; init; }

    /// <summary>Must be held in a hand to work (tomes and handheld weapons). Mounted parts need no hand.</summary>
    public bool Handheld { get; init; }

    /// <summary>Extra hands this part provides while operational (an extra limb); whatever they hold stops working while it is disabled.</summary>
    public int ExtraHands { get; init; }

    /// <summary>Handhelds this part can stow (a harness); a stowed item is inactive until swapped into a hand.</summary>
    public int StowSlots { get; init; }

    /// <summary>Exists only when conjured by a spell (e.g. the Iron Blade): never fabricated, researched, or shaped.</summary>
    public bool ConjuredOnly { get; init; }
    public bool IsSpellAmplifier { get; init; }

    /// <summary>Cargo units this component can carry (fast-iron, consumables). 0 = not a cargo component.</summary>
    public int CargoCapacity { get; init; }

    /// <summary>Fast-iron needed to shape this component in the field. 0 = derived from weight (see ShapingRules).</summary>
    public int FastIronCost { get; init; }

    /// <summary>Fast-iron needed to shape a full ammo refill for this weapon in the field.</summary>
    public int AmmoRefillFastIronCost { get; init; } = 1;

    public int MaxAmmo { get; init; }
    public IReadOnlyList<string> GrantedActionIds { get; init; } = [];
}
