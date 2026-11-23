namespace ToolsAndTomes.Core.Definitions;

public sealed record EquipmentDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ComponentCategory Category { get; init; }

    public int MaxDurability { get; init; } = 1;
    public int WitTax { get; init; }
    public int BonusWitCapacity { get; init; }
    public double Weight { get; init; }

    public int BonusProtectionDice { get; init; }
    public double DisplacementResistance { get; init; }
    public double MoveSpeedBonus { get; init; }

    public double HeatCapacityContribution { get; init; }
    public double HeatDissipationPerSecond { get; init; }

    public int BonusMoteStorage { get; init; }
    public double MoteGenerationRateBonus { get; init; }

    public bool IsCore { get; init; }
    public bool IsArmor { get; init; }
    public bool IsSpellbook { get; init; }
    public bool IsSpellAmplifier { get; init; }

    public int MaxAmmo { get; init; }
    public IReadOnlyList<string> GrantedActionIds { get; init; } = [];
}
