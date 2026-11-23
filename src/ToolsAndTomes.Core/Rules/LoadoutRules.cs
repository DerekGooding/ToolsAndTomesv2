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
        var template = loadout.Template;
        double totalWeight = template.BaseWeight + loadout.Installed.Sum(e => e.Definition.Weight);

        var operational = loadout.Installed.Where(e => e.IsOperational).ToList();

        bool hasSpellbook = operational.Any(e => e.Definition.IsSpellbook);
        bool hasAmplifier = operational.Any(e => e.Definition.IsSpellAmplifier);
        bool isCoreOperational = operational.Any(e => e.Definition.IsCore);

        int maxWit = hasSpellbook ? BaseWitWithSpellbook : BaseWitWithoutSpellbook;
        int witTax = operational.Sum(e => e.Definition.WitTax);
        int maxMotes = DefaultBaseMoteStorage + operational.Sum(e => e.Definition.BonusMoteStorage);
        int bonusProtectionDice = operational.Sum(e => e.Definition.BonusProtectionDice);

        double speedBonus = operational.Sum(e => e.Definition.MoveSpeedBonus);
        double weightPenalty = Math.Max(0, totalWeight - 45.0) * 0.02;
        double moveSpeed = Math.Max(1.2, template.BaseMoveSpeed + speedBonus - weightPenalty);

        double dispResist = Math.Clamp(
            template.BaseDisplacementResistance + operational.Sum(e => e.Definition.DisplacementResistance),
            0.0,
            0.85);

        double heatDissipation = template.BaseHeatDissipationPerSecond +
            operational.Sum(e => e.Definition.HeatDissipationPerSecond);

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
            HeatCapacity = template.BaseHeatCapacity,
            HeatDissipationPerSecond = heatDissipation
        };
    }
}
