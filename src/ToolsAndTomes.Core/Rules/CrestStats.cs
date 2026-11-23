namespace ToolsAndTomes.Core.Rules;

public sealed record CrestStats
{
    public double TotalWeight { get; init; }
    public double MoveSpeed { get; init; }
    public double DisplacementResistance { get; init; }
    public int BonusProtectionDice { get; init; }

    public bool HasSpellbook { get; init; }
    public bool HasSpellAmplifier { get; init; }
    public bool IsCoreOperational { get; init; }

    public int MaxWit { get; init; }
    public int EquipmentWitTax { get; init; }
    public int MaxMotes { get; init; }

    public double HeatCapacity { get; init; }
    public double HeatDissipationPerSecond { get; init; }

    public int AvailableWit(int reservedWit) => MaxWit - EquipmentWitTax - reservedWit;
}
