namespace ToolsAndTomes.Core.Rules;

public sealed record CrestStats
{
    public int TotalWeight { get; init; }

    /// <summary>Tabletop units moved per beat.</summary>
    public int MoveSpeed { get; init; }

    /// <summary>Tabletop units subtracted from incoming push/pull.</summary>
    public int DisplacementResistance { get; init; }
    public int BonusProtectionDice { get; init; }

    public bool HasSpellbook { get; init; }
    public bool HasSpellAmplifier { get; init; }
    public bool IsCoreOperational { get; init; }

    public int MaxWit { get; init; }
    public int EquipmentWitTax { get; init; }
    public int MaxMotes { get; init; }
    public int MoteGenerationIntervalBeats { get; init; }

    public int HeatCapacity { get; init; }
    public int HeatDissipationPerBeat { get; init; }

    public int AvailableWit(int reservedWit) => MaxWit - EquipmentWitTax - reservedWit;
}
