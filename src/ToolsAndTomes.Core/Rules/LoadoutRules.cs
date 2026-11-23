using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

public static class LoadoutRules
{
    public const int BaseWitWithoutSpellbook = 5;
    public const int BaseWitWithSpellbook = 10;
    public const int DefaultBaseMoteStorage = 3;
    public const int MoteGenerationIntervalBeats = 5;
    public const int MinMoteGenerationIntervalBeats = 1;

    /// <summary>Total weight a Crest can carry before movement is penalized.</summary>
    public const int WeightPenaltyThreshold = 80;

    /// <summary>Each full increment of weight above the threshold costs 1 unit of movement per beat.</summary>
    public const int WeightPerMovePenalty = 60;
    public const int MinMoveSpeed = 1;

    public const int MinHeatCapacity = 4;
    public const int MinHeatDissipationPerBeat = 1;

    public static CrestStats ComputeStats(CrestLoadout loadout)
    {
        // All installed components contribute physical mass, even if disabled.
        int totalWeight = loadout.Installed.Sum(e => e.Definition.Weight);
        var operational = loadout.Installed.Where(e => e.IsOperational).ToList();

        bool hasSpellbook = operational.Any(e => e.Definition.IsSpellbook);
        bool hasAmplifier = operational.Any(e => e.Definition.IsSpellAmplifier);
        bool isCoreOperational = operational.Any(e => e.Definition.IsCore);

        int basePilotWit = hasSpellbook ? BaseWitWithSpellbook : BaseWitWithoutSpellbook;
        int maxWit = basePilotWit + operational.Sum(e => e.Definition.BonusWitCapacity);
        int witTax = operational.Sum(e => e.Definition.WitTax);

        int maxMotes = DefaultBaseMoteStorage + operational.Sum(e => e.Definition.BonusMoteStorage);
        int moteInterval = Math.Max(
            MinMoteGenerationIntervalBeats,
            MoteGenerationIntervalBeats - operational.Sum(e => e.Definition.MoteIntervalReductionBeats));

        int bonusProtectionDice = operational.Sum(e => e.Definition.BonusProtectionDice);

        int speedFromComponents = operational.Sum(e => e.Definition.MoveSpeedBonus);
        int weightPenalty = Math.Max(0, totalWeight - WeightPenaltyThreshold) / WeightPerMovePenalty;
        int moveSpeed = Math.Max(MinMoveSpeed, speedFromComponents - weightPenalty);

        int dispResist = Math.Max(0, operational.Sum(e => e.Definition.DisplacementResistance));

        int heatCapacity = Math.Max(MinHeatCapacity, operational.Sum(e => e.Definition.HeatCapacityContribution));
        int heatDissipation = Math.Max(MinHeatDissipationPerBeat, operational.Sum(e => e.Definition.HeatDissipationPerBeat));

        return new CrestStats
        {
            TotalWeight = totalWeight,
            MoveSpeed = moveSpeed,
            DisplacementResistance = dispResist,
            BonusProtectionDice = bonusProtectionDice,
            HasSpellbook = hasSpellbook,
            HasSpellAmplifier = hasAmplifier,
            IsCoreOperational = isCoreOperational,
            MaxWit = maxWit,
            EquipmentWitTax = witTax,
            MaxMotes = maxMotes,
            MoteGenerationIntervalBeats = moteInterval,
            HeatCapacity = heatCapacity,
            HeatDissipationPerBeat = heatDissipation
        };
    }
}
