using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Content;

/// <summary>
/// Checks content against the rules the engine relies on. Any content source — hand-written JSON, mods,
/// or generated content — must pass before it can be played.
/// </summary>
public static class ContentValidator
{
    public static IReadOnlyList<string> Validate(GameCatalog catalog)
    {
        var errors = new List<string>();
        var r = catalog.Rules;

        // Rules
        if (r.MoteGenerationIntervalBeats < 1) errors.Add("rules: moteGenerationIntervalBeats must be at least 1.");
        if (r.MinMoteGenerationIntervalBeats < 1) errors.Add("rules: minMoteGenerationIntervalBeats must be at least 1.");
        if (r.WeightPerMovePenalty < 1) errors.Add("rules: weightPerMovePenalty must be at least 1.");
        if (r.ProtectionSaveTarget is < 2 or > 6) errors.Add("rules: protectionSaveTarget must be 2-6 (1 always fails, 6 always saves).");
        if (r.OverheatClearPercent is < 0 or > 100) errors.Add("rules: overheatClearPercent must be 0-100.");

        // Actions
        foreach (var a in catalog.Actions.Values)
        {
            string at = $"action '{a.Id}'";
            if (a.ImpactBeats < 1) errors.Add($"{at}: impactBeats must be at least 1 (every action has an impact window).");
            if (a.WindupBeats < 0 || a.RecoveryBeats < 0 || a.CooldownBeats < 0) errors.Add($"{at}: beat counts cannot be negative.");
            if (a.IsReactive && a.WindupBeats != 0) errors.Add($"{at}: reactions must have windupBeats 0.");
            if (a.Range < 0 || a.AreaRadius < 0) errors.Add($"{at}: range and areaRadius cannot be negative.");
            if (a.WitReserve < 0 || a.MoteCost < 0 || a.AmmoCost < 0) errors.Add($"{at}: costs cannot be negative.");
            if (a.HitCount < 1) errors.Add($"{at}: hitCount must be at least 1.");
            if (a.MoteCost > 0 && a.MoteCost > MaxPossibleMoteStorage(catalog))
                errors.Add($"{at}: moteCost {a.MoteCost} exceeds the storage of a Crest carrying one of every component.");
            if (!catalog.Equipment.Values.Any(e => e.GrantedActionIds.Contains(a.Id)))
                errors.Add($"{at}: no component grants this action.");
        }

        // Equipment
        foreach (var e in catalog.Equipment.Values)
        {
            string at = $"component '{e.Id}'";
            if (e.MaxDurability < 1) errors.Add($"{at}: maxDurability must be at least 1.");
            if (e.Weight < 0 || e.WitTax < 0) errors.Add($"{at}: weight and witTax cannot be negative.");
            if (e.MovementWitCost > 0 && !e.IsLocomotion) errors.Add($"{at}: movementWitCost requires isLocomotion.");
            foreach (var id in e.GrantedActionIds.Where(id => !catalog.Actions.ContainsKey(id)))
                errors.Add($"{at}: grants unknown action '{id}'.");
        }

        // Builds
        foreach (var c in catalog.StarterConfigurations.Values)
            ValidateConfiguration(catalog, c, $"configuration '{c.Id}'", errors);
        foreach (var en in catalog.Enemies.Values)
            ValidateConfiguration(catalog, en.Assembly, $"enemy '{en.Id}'", errors);

        return errors;
    }

    public static List<string> FindDuplicateIds(
        IEnumerable<ActionDefinition> actions,
        IEnumerable<EquipmentDefinition> equipment,
        IEnumerable<CrestConfiguration> configurations,
        IEnumerable<EnemyDefinition> enemies)
    {
        var errors = new List<string>();
        void Check(string kind, IEnumerable<string> ids) =>
            errors.AddRange(ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => $"duplicate {kind} id '{g.Key}'."));

        Check("action", actions.Select(a => a.Id));
        Check("component", equipment.Select(e => e.Id));
        Check("configuration", configurations.Select(c => c.Id));
        Check("enemy", enemies.Select(e => e.Id));
        return errors;
    }

    private static void ValidateConfiguration(GameCatalog catalog, CrestConfiguration config, string at, List<string> errors)
    {
        foreach (var id in config.ComponentIds.Where(id => !catalog.Equipment.ContainsKey(id)))
            errors.Add($"{at}: unknown component '{id}'.");

        if (!config.ComponentIds.Any(id => catalog.Equipment.TryGetValue(id, out var e) && e.IsCore))
            errors.Add($"{at}: has no Core.");

        foreach (var rule in config.ReactionRules)
        {
            if (!catalog.Actions.TryGetValue(rule.ActionId, out var def))
                errors.Add($"{at}: reaction rule uses unknown action '{rule.ActionId}'.");
            else if (!def.IsReactive)
                errors.Add($"{at}: reaction rule action '{rule.ActionId}' is not a reaction.");
        }
    }

    private static int MaxPossibleMoteStorage(GameCatalog catalog) =>
        catalog.Rules.BaseMoteStorage + catalog.Equipment.Values.Sum(e => Math.Max(0, e.BonusMoteStorage));
}
