namespace ToolsAndTomes.Core.Rules;

public sealed record CrestStats
{
    public int TotalWeight { get; init; }

    /// <summary>Cargo units available in operational cargo components.</summary>
    public int CargoCapacity { get; init; }

    /// <summary>Cargo units currently filled.</summary>
    public int CargoUsed { get; init; }

    /// <summary>Combined load capacity of operational locomotion.</summary>
    public int LoadCapacity { get; init; }

    /// <summary>Tabletop units moved per beat.</summary>
    public int MoveSpeed { get; init; }

    /// <summary>Tabletop units subtracted from incoming push/pull.</summary>
    public int DisplacementResistance { get; init; }
    public int BonusProtectionDice { get; init; }

    public bool HasTome { get; init; }

    /// <summary>Operational tomes held in a working hand.</summary>
    public int TomesHeld { get; init; }

    /// <summary>Hands available: the Crest's own plus operational extra limbs.</summary>
    public int Hands { get; init; }

    /// <summary>Handheld components that aren't held (more than the Crest has hands); they don't work.</summary>
    public int UnheldHandhelds { get; init; }
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
