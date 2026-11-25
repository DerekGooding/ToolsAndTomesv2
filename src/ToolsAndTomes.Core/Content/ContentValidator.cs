using ToolsAndTomes.Core.Instances;
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
            if (!catalog.Rules.CoreSpellIds.Contains(a.Id) && !catalog.Equipment.Values.Any(e => e.GrantedActionIds.Contains(a.Id)))
                errors.Add($"{at}: no component grants this action (and it isn't a core spell).");
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
        foreach (var p in catalog.Pilots.Values)
            ValidateConfiguration(catalog, p.Assembly, $"pilot '{p.Id}'", errors);

        // Exotic materials & research
        foreach (var m in catalog.Materials.Values)
        {
            string at = $"material '{m.Id}'";
            if (m.SamplesToResearch < 1) errors.Add($"{at}: samplesToResearch must be at least 1.");
            if (m.UnlocksComponentIds.Count == 0) errors.Add($"{at}: unlocks nothing.");
            foreach (var id in m.UnlocksComponentIds.Where(id => !catalog.Equipment.ContainsKey(id)))
                errors.Add($"{at}: unlocks unknown component '{id}'.");
            foreach (var id in m.UnlocksComponentIds.Where(catalog.BaselineComponentIds.Contains))
                errors.Add($"{at}: unlocks '{id}', which is already known from a starter build.");
        }

        if (catalog.Materials.Count > 0)
        {
            var unlockable = catalog.Materials.Values.SelectMany(m => m.UnlocksComponentIds).ToHashSet();
            foreach (var e in catalog.Equipment.Values)
            {
                if (!e.ConjuredOnly && !catalog.BaselineComponentIds.Contains(e.Id) && !unlockable.Contains(e.Id))
                    errors.Add($"component '{e.Id}': not in a starter build and not unlocked by any material (unobtainable).");
            }
        }

        // Obstacles
        foreach (var o in catalog.Obstacles.Values)
        {
            string at = $"obstacle '{o.Id}'";
            if (o.Shape == ObstacleShape.Circle && o.Radius <= 0) errors.Add($"{at}: a circle needs a radius.");
            if (o.Shape == ObstacleShape.Wall && o.Thickness <= 0) errors.Add($"{at}: a wall needs a thickness.");
            if (o.Durability < 1) errors.Add($"{at}: durability must be at least 1.");
            if (o.Height == ObstacleHeight.Tall && o.CoverDice > 0) errors.Add($"{at}: Tall obstacles block line of fire; cover dice are for Low ones.");
        }
        if (catalog.Obstacles.Values.Count(o => o.IsRubble) > 1)
            errors.Add("obstacles: only one obstacle type can be rubble.");
        foreach (var a in catalog.Actions.Values.Where(a => a.RaisesObstacleId is not null))
        {
            if (!catalog.Obstacles.TryGetValue(a.RaisesObstacleId!, out var raised) || raised.Shape != ObstacleShape.Wall)
                errors.Add($"action '{a.Id}': raises '{a.RaisesObstacleId}', which must be a wall obstacle.");
            if (a.WallLength <= 0) errors.Add($"action '{a.Id}': a raised wall needs a wallLength.");
        }
        foreach (var enc in catalog.Encounters.Values)
            ValidateLayout(catalog, enc, errors);

        // Tomes, hands, and spells
        foreach (var id in catalog.Rules.CoreSpellIds.Where(id => !catalog.Actions.ContainsKey(id)))
            errors.Add($"rules: core spell '{id}' is not an action.");
        if (catalog.Rules.BaseHands < 0) errors.Add("rules: baseHands cannot be negative.");
        foreach (var e in catalog.Equipment.Values)
        {
            string at = $"component '{e.Id}'";
            if (e.IsTome && !e.Handheld) errors.Add($"{at}: a tome must be handheld.");
            if (e.Handheld && (e.ExtraHands > 0 || e.StowSlots > 0)) errors.Add($"{at}: a handheld can't be a limb or harness.");
            if (e.ExtraHands < 0 || e.StowSlots < 0) errors.Add($"{at}: extraHands and stowSlots cannot be negative.");
            if (e.ConjuredOnly && catalog.BaselineComponentIds.Contains(e.Id)) errors.Add($"{at}: conjured-only parts can't be in a starter build.");
        }
        foreach (var a in catalog.Actions.Values)
        {
            string at = $"action '{a.Id}'";
            if (a.ConjuresComponentId is { } cid &&
                (!catalog.Equipment.TryGetValue(cid, out var conjured) || !conjured.Handheld || !conjured.ConjuredOnly))
                errors.Add($"{at}: conjures '{cid}', which must be a handheld, conjured-only component.");
            if (a.ChainJumps > 0 && a.ChainRange <= 0) errors.Add($"{at}: chainJumps needs a chainRange.");
            if (a.ProtectionBonusDice > 0 && a.BuffDurationBeats <= 0) errors.Add($"{at}: protectionBonusDice needs buffDurationBeats.");
            if (a.SlowAmount > 0 && a.SlowBeats <= 0) errors.Add($"{at}: slowAmount needs slowBeats.");
            if (a.IsSwap && !catalog.Equipment.Values.Any(e => e.StowSlots > 0 && e.GrantedActionIds.Contains(a.Id)))
                errors.Add($"{at}: a swap must be granted by a harness (a component with stowSlots).");
        }

        // Consumables & field shaping
        foreach (var c in catalog.Consumables.Values)
        {
            string at = $"consumable '{c.Id}'";
            if (c.CargoSize < 1) errors.Add($"{at}: cargoSize must be at least 1.");
            if (c.FastIronCost < 1) errors.Add($"{at}: fastIronCost must be at least 1.");
            if (!catalog.Actions.TryGetValue(c.UseActionId, out var use))
                errors.Add($"{at}: unknown useActionId '{c.UseActionId}'.");
            else if (use.ConsumableItemId != c.Id)
                errors.Add($"{at}: action '{use.Id}' must declare consumableItemId '{c.Id}'.");
        }

        foreach (var a in catalog.Actions.Values.Where(a => a.ConsumableItemId is not null))
        {
            if (!catalog.Consumables.ContainsKey(a.ConsumableItemId!))
                errors.Add($"action '{a.Id}': consumes unknown consumable '{a.ConsumableItemId}'.");
            if (!catalog.Equipment.Values.Any(e => e.CargoCapacity > 0 && e.GrantedActionIds.Contains(a.Id)))
                errors.Add($"action '{a.Id}': consumable actions must be granted by a cargo component.");
        }

        foreach (var cfg in catalog.StarterConfigurations.Values)
        {
            foreach (var entry in cfg.Cargo.Where(e => e.Kind == CargoKind.Consumable))
            {
                if (entry.ItemId is null || !catalog.Consumables.ContainsKey(entry.ItemId))
                    errors.Add($"configuration '{cfg.Id}': cargo has unknown consumable '{entry.ItemId}'.");
            }
        }

        // Missions
        foreach (var m in catalog.Missions.Values)
        {
            string at = $"mission '{m.Id}'";
            if (m.EncounterIds.Count == 0) errors.Add($"{at}: has no encounters.");
            if (m.RestBeats < 0) errors.Add($"{at}: restBeats cannot be negative.");
            foreach (var id in m.EncounterIds.Where(id => !catalog.Encounters.ContainsKey(id)))
                errors.Add($"{at}: unknown encounter '{id}'.");
            foreach (var id in m.RequiredResearch.Where(id => !catalog.Materials.ContainsKey(id)))
                errors.Add($"{at}: requires unknown material '{id}'.");
            foreach (var id in m.RequiredMissions.Where(id => !catalog.Missions.ContainsKey(id)))
                errors.Add($"{at}: requires unknown mission '{id}'.");
            if (RequiresItself(catalog, m.Id, m.Id, []))
                errors.Add($"{at}: its required missions lead back to itself, so it can never unlock.");
            foreach (var reward in m.CompletionRewards.Where(x => !catalog.Materials.ContainsKey(x.MaterialId)))
                errors.Add($"{at}: rewards unknown material '{reward.MaterialId}'.");
        }

        if (catalog.Missions.Count > 0 && !catalog.Missions.Values.Any(m => m.RequiredResearch.Count == 0 && m.RequiredMissions.Count == 0))
            errors.Add("missions: at least one mission must be available without research or earlier missions.");

        // Encounters
        foreach (var enc in catalog.Encounters.Values)
        {
            string at = $"encounter '{enc.Id}'";
            if (enc.ArenaWidth < 4 || enc.ArenaHeight < 4) errors.Add($"{at}: arena must be at least 4x4.");
            if (enc.Enemies.Count == 0) errors.Add($"{at}: has no enemies.");
            foreach (var s in enc.AllyStarts.Where(s => s.X < 0 || s.Y < 0 || s.X > enc.ArenaWidth || s.Y > enc.ArenaHeight))
                errors.Add($"{at}: ally start ({s.X}, {s.Y}) is outside the arena.");
            foreach (var p in enc.Enemies.Where(p => !catalog.Enemies.ContainsKey(p.EnemyId)))
                errors.Add($"{at}: unknown enemy '{p.EnemyId}'.");

            var rewardIds = enc.Rewards.Guaranteed.Select(g => g.MaterialId).Concat(enc.Rewards.Pool.Select(p => p.MaterialId));
            foreach (var id in rewardIds.Where(id => !catalog.Materials.ContainsKey(id)).Distinct())
                errors.Add($"{at}: rewards unknown material '{id}'.");
            if (enc.Rewards.Guaranteed.Any(g => g.Count < 1)) errors.Add($"{at}: guaranteed reward counts must be at least 1.");
            if (enc.Rewards.Pool.Any(p => p.Weight < 1)) errors.Add($"{at}: reward pool weights must be at least 1.");
            if (enc.Rewards.Draws < 0) errors.Add($"{at}: reward draws cannot be negative.");
            if (enc.Rewards.Draws > 0 && enc.Rewards.Pool.Count == 0) errors.Add($"{at}: reward draws need a pool.");
        }

        return errors;
    }

    /// <summary>
    /// An encounter's obstacles: known, placeable types with the right points, inside the arena; no start position
    /// overlaps an obstacle; and every enemy has a route to the player's start.
    /// </summary>
    private static void ValidateLayout(GameCatalog catalog, EncounterDefinition enc, List<string> errors)
    {
        if (enc.Obstacles.Count == 0)
            return;
        string at = $"encounter '{enc.Id}'";
        var placed = new List<Obstacle>();
        foreach (var (p, i) in enc.Obstacles.Select((p, i) => (p, i)))
        {
            if (!catalog.Obstacles.TryGetValue(p.Type, out var def))
            {
                errors.Add($"{at}: unknown obstacle '{p.Type}'.");
                continue;
            }
            if (def.ConjuredOnly)
                errors.Add($"{at}: '{p.Type}' is only raised by spells.");
            bool shaped = def.Shape == ObstacleShape.Circle ? p.At is not null : p.From is not null && p.To is not null;
            if (!shaped)
            {
                errors.Add($"{at}: obstacle {i + 1} ('{p.Type}') needs {(def.Shape == ObstacleShape.Circle ? "'at'" : "'from' and 'to'")}.");
                continue;
            }
            var o = Obstacle.FromPlacement($"o{i}", def, p);
            if (new[] { o.A, o.B }.Any(v => v.X < 0 || v.Y < 0 || v.X > enc.ArenaWidth || v.Y > enc.ArenaHeight))
                errors.Add($"{at}: obstacle {i + 1} ('{p.Type}') lies outside the arena.");
            placed.Add(o);
        }

        var terrain = new Simulation.Terrain(placed, enc.ArenaWidth, enc.ArenaHeight, catalog.Rules, catalog.Rubble);
        var starts = new List<(string Who, Vec2 Where)> { ("the player's start", enc.PlayerStart) };
        for (int i = 0; i < Campaign.MissionRun.MaxAllies; i++)
            starts.Add(($"ally start {i + 1}", enc.AllyStart(i)));
        starts.AddRange(enc.Enemies.Select((e, i) => ($"enemy {i + 1} ('{e.EnemyId}')", e.Position)));
        foreach (var (who, where) in starts.Where(s => !terrain.IsFree(s.Where)))
            errors.Add($"{at}: {who} at ({where.X}, {where.Y}) overlaps an obstacle.");

        foreach (var (e, i) in enc.Enemies.Select((e, i) => (e, i)))
        {
            var path = terrain.FindPath(e.Position, enc.PlayerStart);
            var legs = new[] { e.Position }.Concat(path).ToList();
            bool reaches = path[^1].DistanceTo(enc.PlayerStart) < 0.01 &&
                           legs.Zip(legs.Skip(1)).All(l => terrain.SegmentClear(l.First, l.Second));
            if (!reaches)
                errors.Add($"{at}: enemy {i + 1} ('{e.EnemyId}') has no route to the player's start.");
        }
    }

    private static bool RequiresItself(GameCatalog catalog, string origin, string current, HashSet<string> seen)
    {
        if (!seen.Add(current) || !catalog.Missions.TryGetValue(current, out var mission))
            return false;
        return mission.RequiredMissions.Any(id => id == origin || RequiresItself(catalog, origin, id, seen));
    }

    public static List<string> FindDuplicateIds(
        IEnumerable<ActionDefinition> actions,
        IEnumerable<EquipmentDefinition> equipment,
        IEnumerable<CrestConfiguration> configurations,
        IEnumerable<EnemyDefinition> enemies,
        IEnumerable<ExoticMaterialDefinition>? materials = null,
        IEnumerable<EncounterDefinition>? encounters = null,
        IEnumerable<ConsumableDefinition>? consumables = null,
        IEnumerable<MissionDefinition>? missions = null,
        IEnumerable<PilotDefinition>? pilots = null,
        IEnumerable<ObstacleDefinition>? obstacles = null)
    {
        var errors = new List<string>();
        void Check(string kind, IEnumerable<string> ids) =>
            errors.AddRange(ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => $"duplicate {kind} id '{g.Key}'."));

        Check("action", actions.Select(a => a.Id));
        Check("component", equipment.Select(e => e.Id));
        Check("configuration", configurations.Select(c => c.Id));
        Check("enemy", enemies.Select(e => e.Id));
        Check("material", (materials ?? []).Select(m => m.Id));
        Check("encounter", (encounters ?? []).Select(e => e.Id));
        Check("consumable", (consumables ?? []).Select(c => c.Id));
        Check("mission", (missions ?? []).Select(m => m.Id));
        Check("pilot", (pilots ?? []).Select(p => p.Id));
        Check("obstacle", (obstacles ?? []).Select(o => o.Id));
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

        EquipmentDefinition? Part(int i) =>
            i >= 0 && i < config.ComponentIds.Count && catalog.Equipment.TryGetValue(config.ComponentIds[i], out var e) ? e : null;
        foreach (var h in config.Holdings)
        {
            var held = Part(h.Held);
            var holder = Part(h.Holder);
            if (held is null || holder is null)
                errors.Add($"{at}: holding ({h.Held} held by {h.Holder}) points outside the component list.");
            else if (!held.Handheld)
                errors.Add($"{at}: '{held.Id}' is held by '{holder.Id}' but isn't handheld.");
            else if (holder.ExtraHands <= 0 && holder.StowSlots <= 0)
                errors.Add($"{at}: '{holder.Id}' holds '{held.Id}' but is neither a limb nor a harness.");
        }
        foreach (var group in config.Holdings.GroupBy(h => h.Holder))
        {
            var holder = Part(group.Key);
            int capacity = holder is null ? 0 : Math.Max(holder.ExtraHands, holder.StowSlots);
            if (holder is not null && group.Count() > capacity)
                errors.Add($"{at}: '{holder.Id}' holds {group.Count()} items but has room for {capacity}.");
        }
        if (config.Holdings.GroupBy(h => h.Held).Any(g => g.Count() > 1))
            errors.Add($"{at}: a component is held twice.");
    }

    private static int MaxPossibleMoteStorage(GameCatalog catalog) =>
        catalog.Rules.BaseMoteStorage + catalog.Equipment.Values.Sum(e => Math.Max(0, e.BonusMoteStorage));
}
