using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

public static class LoadoutRules
{
    public const int BaseWitWithoutSpellbook = 5;
    public const int BaseWitWithSpellbook = 10;
    public const int DefaultBaseMoteStorage = 3;
    public const int MoteGenerationIntervalBeats = 5;
    public const int MinMoteGenerationIntervalBeats = 1;

    /// <summary>
    /// Each full increment of weight above the operational legs' combined load capacity costs 1 unit of
    /// movement per beat. Heavy builds stay playable by fitting sturdier legs.
    /// </summary>
    public const int WeightPerMovePenalty = 40;

    /// <summary>Minimum speed while any locomotion is operational (heaviness slows, never immobilizes).</summary>
    public const int MinMoveSpeed = 1;

    public const int MinHeatCapacity = 4;
    public const int MinHeatDissipationPerBeat = 1;

    /// <summary>
    /// Workshop advisories. None of these prevent launching (except a missing Core, which is reported
    /// separately by <see cref="CrestStats.IsCoreOperational"/>); they warn about risky builds.
    /// </summary>
    public static IReadOnlyList<string> GetWarnings(CrestLoadout loadout)
    {
        var warnings = new List<string>();
        var order = loadout.DamageOrder;

        int firstCore = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].Definition.IsCore)
            {
                firstCore = i;
                break;
            }
        }

        if (firstCore < 0)
        {
            warnings.Add("No Core installed: this Crest cannot fight.");
        }
        else if (order.Skip(firstCore + 1).Any(e => !e.Definition.IsCore))
        {
            warnings.Add("Core is not last in the Damage Order: it will take damage before the components after it.");
        }

        var stats = ComputeStats(loadout);
        int available = stats.AvailableWit(0);
        if (available < 0)
            warnings.Add($"Wit over budget: passive Wit Tax {stats.EquipmentWitTax} exceeds capacity {stats.MaxWit}.");

        return warnings;
    }

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
        var locomotion = operational.Where(e => e.Definition.IsLocomotion).ToList();
        int loadCapacity = locomotion.Sum(e => e.Definition.LoadCapacity);
        int weightPenalty = Math.Max(0, totalWeight - loadCapacity) / WeightPerMovePenalty;
        int moveSpeed = locomotion.Count == 0 ? 0 : Math.Max(MinMoveSpeed, speedFromComponents - weightPenalty);

        int dispResist = Math.Max(0, operational.Sum(e => e.Definition.DisplacementResistance));

        int heatCapacity = Math.Max(MinHeatCapacity, operational.Sum(e => e.Definition.HeatCapacityContribution));
        int heatDissipation = Math.Max(MinHeatDissipationPerBeat, operational.Sum(e => e.Definition.HeatDissipationPerBeat));

        return new CrestStats
        {
            TotalWeight = totalWeight,
            LoadCapacity = loadCapacity,
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
