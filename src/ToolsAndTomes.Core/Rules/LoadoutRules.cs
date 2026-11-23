using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

public static class LoadoutRules
{
    public const int BaseWitWithoutSpellbook = 5;
    public const int BaseWitWithSpellbook = 10;
    public const int DefaultBaseMoteStorage = 3;
    public const int MoteGenerationIntervalTicks = ActionCatalog.TicksPerSecond * 5; // 5 seconds = 100 ticks

    public static CrestStats ComputeStats(CrestLoadout loadout)
    {
        // All installed components contribute physical mass, even if disabled.
        double totalWeight = loadout.Installed.Sum(e => e.Definition.Weight);
        var operational = loadout.Installed.Where(e => e.IsOperational).ToList();

        bool hasSpellbook = operational.Any(e => e.Definition.IsSpellbook);
        bool hasAmplifier = operational.Any(e => e.Definition.IsSpellAmplifier);
        bool isCoreOperational = operational.Any(e => e.Definition.IsCore);

        int basePilotWit = hasSpellbook ? BaseWitWithSpellbook : BaseWitWithoutSpellbook;
        int maxWit = basePilotWit + operational.Sum(e => e.Definition.BonusWitCapacity);
        int witTax = operational.Sum(e => e.Definition.WitTax);

        int maxMotes = DefaultBaseMoteStorage + operational.Sum(e => e.Definition.BonusMoteStorage);
        double moteRateBonus = Math.Max(0.0, operational.Sum(e => e.Definition.MoteGenerationRateBonus));
        int moteIntervalTicks = Math.Max(20, (int)Math.Round(MoteGenerationIntervalTicks / (1.0 + moteRateBonus)));

        int bonusProtectionDice = operational.Sum(e => e.Definition.BonusProtectionDice);

        double speedFromComponents = operational.Sum(e => e.Definition.MoveSpeedBonus);
        double weightPenalty = Math.Max(0, totalWeight - 45.0) * 0.02;
        double moveSpeed = Math.Max(0.8, speedFromComponents - weightPenalty);

        double dispResist = Math.Clamp(
            operational.Sum(e => e.Definition.DisplacementResistance),
            0.0,
            0.85);

        double heatCapacity = Math.Max(4.0, operational.Sum(e => e.Definition.HeatCapacityContribution));
        double heatDissipation = Math.Max(0.5, operational.Sum(e => e.Definition.HeatDissipationPerSecond));

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
            MoteGenerationIntervalTicks = moteIntervalTicks,
            HeatCapacity = heatCapacity,
            HeatDissipationPerSecond = heatDissipation
        };
    }
}
