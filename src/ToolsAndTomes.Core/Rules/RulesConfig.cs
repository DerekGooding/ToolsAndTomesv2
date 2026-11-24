namespace ToolsAndTomes.Core.Rules;

/// <summary>
/// Every global tuning number in the rules, in one place. Content (actions, components, builds) lives in
/// the catalog; this record holds the numbers that apply to all of it. Loaded from <c>rules.json</c>.
/// All values are small integers so the same rules can be run by hand at a table.
/// </summary>
public sealed record RulesConfig
{
    public static RulesConfig Default { get; } = new();

    // ---- Wit & Motes ----
    public int BaseWitWithoutSpellbook { get; init; } = 5;
    public int BaseWitWithSpellbook { get; init; } = 10;
    public int BaseMoteStorage { get; init; } = 3;
    public int MoteGenerationIntervalBeats { get; init; } = 5;
    public int MinMoteGenerationIntervalBeats { get; init; } = 1;

    // ---- Movement & weight ----
    /// <summary>Each full increment of weight above the legs' load capacity costs 1 movement per beat.</summary>
    public int WeightPerMovePenalty { get; init; } = 40;

    /// <summary>Minimum speed while any locomotion is operational (heaviness slows, never immobilizes).</summary>
    public int MinMoveSpeed { get; init; } = 1;

    // ---- Heat ----
    public int MinHeatCapacity { get; init; } = 4;
    public int MinHeatDissipationPerBeat { get; init; } = 1;
    public int OverheatRecoveryPenaltyBeats { get; init; } = 1;

    /// <summary>Overheat clears once heat falls to this percentage of capacity or less.</summary>
    public int OverheatClearPercent { get; init; } = 40;

    // ---- Fast-iron & cargo ----
    /// <summary>Weight of one unit of carried fast-iron.</summary>
    public int FastIronWeight { get; init; } = 2;

    /// <summary>Cargo units occupied by one unit of fast-iron.</summary>
    public int FastIronCargoSize { get; init; } = 1;

    /// <summary>Fast-iron to restore one point of durability during a rest in the field.</summary>
    public int FastIronPerRepairPoint { get; init; } = 1;

    /// <summary>When a component sets no fastIronCost, shaping it costs ceil(weight / this), minimum 1.</summary>
    public int ComponentWeightPerFastIron { get; init; } = 10;

    // ---- Damage ----
    /// <summary>d6 result needed to save a point of damage (1 always fails, 6 always saves).</summary>
    public int ProtectionSaveTarget { get; init; } = 5;

    /// <summary>Units beyond an action's range at which a committed impact still connects.</summary>
    public int ResolutionRangeTolerance { get; init; } = 1;

    // ---- Interruption ----
    public InterruptionThresholds Interruption { get; init; } = InterruptionThresholds.Default;
}
