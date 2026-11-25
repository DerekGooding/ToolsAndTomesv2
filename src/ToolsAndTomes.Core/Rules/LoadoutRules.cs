using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

public static class LoadoutRules
{
    /// <summary>
    /// Workshop advisories. None of these prevent launching (except a missing Core, which is reported
    /// separately by <see cref="CrestStats.IsCoreOperational"/>); they warn about risky builds.
    /// </summary>
    public static IReadOnlyList<string> GetWarnings(CrestLoadout loadout, RulesConfig? rules = null)
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

        var stats = ComputeStats(loadout, rules);
        int available = stats.AvailableWit(0);
        if (available < 0)
            warnings.Add($"Wit over budget: passive Wit Tax {stats.EquipmentWitTax} exceeds capacity {stats.MaxWit}.");

        if (stats.CargoUsed > stats.CargoCapacity)
            warnings.Add($"Cargo over capacity: {stats.CargoUsed} units loaded, {stats.CargoCapacity} available.");

        var unheld = order.Where(e => e.IsOperational && loadout.HoldStateOf(e) == HoldState.Unheld).ToList();
        if (unheld.Count > 0)
            warnings.Add($"No free hand for {string.Join(", ", unheld.Select(e => e.Definition.Name))}: " +
                         $"{(unheld.Count == 1 ? "it doesn't" : "they don't")} work. Add an extra limb, or remove a handheld.");

        // A limb that takes hits before what it holds can lose it early.
        foreach (var part in order)
        {
            if (loadout.HoldStateOf(part) != HoldState.Limb || loadout.HolderOf(part) is not { } limb)
                continue;
            int limbIndex = IndexOf(order, limb), partIndex = IndexOf(order, part);
            if (limbIndex < partIndex)
                warnings.Add($"LIMB BEFORE ITS ITEM: the {limb.Definition.Name} takes hits before the {part.Definition.Name} it holds. " +
                             $"Lose the limb, lose the {part.Definition.Name}. Move the {limb.Definition.Name} later in the Damage Order.");
        }

        return warnings;
    }

    /// <summary>Derives a Crest's capabilities purely from its operational components. Pure; see <see cref="CrestLoadout.GetStats"/> for the cached form.</summary>
    private static int IndexOf(IReadOnlyList<Instances.EquipmentInstance> order, Instances.EquipmentInstance part)
    {
        for (int i = 0; i < order.Count; i++)
            if (ReferenceEquals(order[i], part))
                return i;
        return -1;
    }

    /// <summary>
    /// The Crest's stats. Only <i>active</i> components count: operational and, if handheld, held in a working
    /// hand (stowed, unheld, and limb-dropped handhelds give nothing). Weight counts everything carried.
    /// </summary>
    public static CrestStats ComputeStats(CrestLoadout loadout, RulesConfig? rules = null)
    {
        var r = rules ?? loadout.Rules;

        // All installed components contribute physical mass, even if disabled, as does carried cargo.
        int totalWeight = loadout.Installed.Sum(e => e.Definition.Weight) + loadout.CargoWeight(r);
        var operational = loadout.Installed.Where(loadout.IsActive).ToList();

        int tomesHeld = operational.Count(e => e.Definition.IsTome);
        bool hasAmplifier = operational.Any(e => e.Definition.IsSpellAmplifier);
        bool isCoreOperational = operational.Any(e => e.Definition.IsCore);
        int hands = r.BaseHands + loadout.Installed.Where(e => e.IsOperational).Sum(e => e.Definition.ExtraHands);
        int unheld = loadout.Installed.Count(e => e.IsOperational && loadout.HoldStateOf(e) == HoldState.Unheld);

        int basePilotWit = tomesHeld > 0 ? r.BaseWitWithTome : r.BaseWitWithoutTome;
        int maxWit = basePilotWit + operational.Sum(e => e.Definition.BonusWitCapacity);
        int witTax = operational.Sum(e => e.Definition.WitTax);

        int maxMotes = r.BaseMoteStorage + operational.Sum(e => e.Definition.BonusMoteStorage);
        int moteInterval = Math.Max(
            r.MinMoteGenerationIntervalBeats,
            r.MoteGenerationIntervalBeats - operational.Sum(e => e.Definition.MoteIntervalReductionBeats));

        int bonusProtectionDice = operational.Sum(e => e.Definition.BonusProtectionDice);

        int speedFromComponents = operational.Sum(e => e.Definition.MoveSpeedBonus);
        var locomotion = operational.Where(e => e.Definition.IsLocomotion).ToList();
        int loadCapacity = locomotion.Sum(e => e.Definition.LoadCapacity);
        int weightPenalty = Math.Max(0, totalWeight - loadCapacity) / r.WeightPerMovePenalty;
        int moveSpeed = locomotion.Count == 0 ? 0 : Math.Max(r.MinMoveSpeed, speedFromComponents - weightPenalty);

        int dispResist = Math.Max(0, operational.Sum(e => e.Definition.DisplacementResistance));

        int heatCapacity = Math.Max(r.MinHeatCapacity, operational.Sum(e => e.Definition.HeatCapacityContribution));
        int heatDissipation = Math.Max(r.MinHeatDissipationPerBeat, operational.Sum(e => e.Definition.HeatDissipationPerBeat));

        return new CrestStats
        {
            TotalWeight = totalWeight,
            LoadCapacity = loadCapacity,
            CargoCapacity = operational.Sum(e => e.Definition.CargoCapacity),
            CargoUsed = loadout.CargoUnitsUsed(r),
            MoveSpeed = moveSpeed,
            DisplacementResistance = dispResist,
            BonusProtectionDice = bonusProtectionDice,
            HasTome = tomesHeld > 0,
            TomesHeld = tomesHeld,
            Hands = hands,
            UnheldHandhelds = unheld,
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
