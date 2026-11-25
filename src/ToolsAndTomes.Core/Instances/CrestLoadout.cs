using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Instances;

/// <summary>Where a component is, as far as hands go.</summary>
public enum HoldState
{
    /// <summary>Not handheld: it works wherever it is mounted.</summary>
    NotHandheld,
    /// <summary>In one of the Crest's own hands.</summary>
    OwnHand,
    /// <summary>Held by an extra limb: it works only while the limb works.</summary>
    Limb,
    /// <summary>Stowed in a harness: inactive until swapped into a hand.</summary>
    Stowed,
    /// <summary>No free hand: inactive.</summary>
    Unheld
}

/// <summary>
/// A Crest is the assembled collection of its installed component instances.
/// It has no predefined chassis or class; all capabilities emerge from its components.
/// The order of <see cref="Installed"/> is the Crest's <b>Damage Order</b>: the defender's chosen
/// sequence in which components absorb penetrating damage.
/// </summary>
/// <remarks>
/// Hands: handheld components (tomes, handheld weapons) work only while held. Handhelds assigned to an extra
/// limb (<see cref="EquipmentDefinition.ExtraHands"/>) or stowed in a harness (<see cref="EquipmentDefinition.StowSlots"/>)
/// are tracked in <see cref="Holders"/>; every other handheld goes in the Crest's own hands
/// (<see cref="RulesConfig.BaseHands"/>) in Damage Order, and any beyond that are unheld.
/// Core spells come from the <see cref="Pilot"/>, which is never installed and can't be damaged.
/// </remarks>
public sealed class CrestLoadout
{
    /// <summary>Instance id of the pilot, the source of core spells.</summary>
    public const string PilotInstanceId = "pilot";

    private readonly Dictionary<string, string> _holders = [];
    private RulesConfig _rules = RulesConfig.Default;
    private EquipmentInstance? _pilot;

    private readonly List<EquipmentInstance> _installed = [];
    private readonly List<ReactionRule> _reactionRules = [];
    private readonly List<(CargoEntry Entry, ConsumableDefinition? Def)> _cargoManifest = [];
    private readonly Dictionary<string, (ConsumableDefinition Def, int Count)> _consumables = [];
    private int _structureVersion;
    private (int Structure, int Operational, RulesConfig Rules, CrestStats Stats)? _statsCache;

    public string Id { get; }
    public string Name { get; set; }

    /// <summary>Installed components, in Damage Order.</summary>
    public IReadOnlyList<EquipmentInstance> Installed => _installed;

    /// <summary>Components in the order they absorb penetrating damage (same as <see cref="Installed"/>).</summary>
    public IReadOnlyList<EquipmentInstance> DamageOrder => _installed;

    /// <summary>Pre-set reactions configured in the Workshop, in priority order.</summary>
    public IReadOnlyList<ReactionRule> ReactionRules => _reactionRules;

    /// <summary>The rules in force (hands, core spells); a Combatant keeps this in step with its own.</summary>
    public RulesConfig Rules
    {
        get => _rules;
        set
        {
            if (ReferenceEquals(_rules, value))
                return;
            _rules = value;
            _pilot = null;
            _structureVersion++;
        }
    }

    /// <summary>The pilot: provides the core spells every pilot knows. Never installed; never damaged.</summary>
    public EquipmentInstance Pilot => _pilot ??= new EquipmentInstance(PilotInstanceId, new EquipmentDefinition
    {
        Id = PilotInstanceId,
        Name = "Pilot",
        Description = "The pilot's own core spells, known in any Crest.",
        Category = ComponentCategory.Arcane,
        MaxDurability = 1,
        GrantedActionIds = _rules.CoreSpellIds
    });

    /// <summary>Handheld instance id → the limb or harness holding it. Unlisted handhelds use the Crest's own hands.</summary>
    public IReadOnlyDictionary<string, string> Holders => _holders;
    public CrestLoadout(string id, string name, IEnumerable<EquipmentInstance>? initialEquipment = null)
    {
        Id = id;
        Name = name;
        if (initialEquipment is not null)
            _installed.AddRange(initialEquipment);
    }

    public static CrestLoadout CreateFromConfiguration(
        CrestConfiguration config,
        GameCatalog catalog,
        string idPrefix = "comp")
    {
        var instances = new List<EquipmentInstance>();
        int index = 1;
        foreach (var compId in config.ComponentIds)
        {
            var def = catalog.GetEquipment(compId);
            instances.Add(new EquipmentInstance($"{idPrefix}_{index++}_{compId}", def));
        }

        var loadout = new CrestLoadout(config.Id, config.Name, instances) { Rules = catalog.Rules };
        foreach (var rule in config.ReactionRules)
            loadout.AddReactionRule(rule);
        loadout.SetCargoManifest(config.Cargo, catalog);
        loadout.ApplyHoldings(config.Holdings);
        return loadout;
    }

    /// <summary>Restores limb and harness assignments saved as indices into the component list (invalid ones are skipped).</summary>
    public void ApplyHoldings(IEnumerable<Holding> holdings)
    {
        foreach (var h in holdings)
        {
            if (h.Held < 0 || h.Held >= _installed.Count || h.Holder < 0 || h.Holder >= _installed.Count)
                continue;
            SetHolder(_installed[h.Held].InstanceId, _installed[h.Holder].InstanceId);
        }
    }

    /// <summary>Exports the assembly; component order (the Damage Order) is preserved.</summary>
    /// <remarks>Conjured parts are never exported; limb and harness assignments are, as component indices.</remarks>
    public CrestConfiguration ExportConfiguration(string? overrideId = null, string? description = null)
    {
        var kept = _installed.Where(e => !e.IsConjured).ToList();
        var index = kept.Select((e, i) => (e.InstanceId, i)).ToDictionary(x => x.InstanceId, x => x.i);
        return new()
        {
            Id = overrideId ?? Id,
            Name = Name,
            Description = description ?? $"Saved assembly ({kept.Count} components).",
            ComponentIds = kept.Select(e => e.Definition.Id).ToList(),
            ReactionRules = _reactionRules.ToList(),
            Cargo = _cargoManifest.Select(m => m.Entry).ToList(),
            Holdings = _holders
                .Where(kv => index.ContainsKey(kv.Key) && index.ContainsKey(kv.Value))
                .Select(kv => new Holding(index[kv.Key], index[kv.Value]))
                .OrderBy(h => h.Held)
                .ToList()
        };
    }

    // ---- Hands --------------------------------------------------------------------------------

    /// <summary>
    /// Where a component is, as far as hands go (see <see cref="HoldState"/>). A <b>disabled</b> handheld lets go
    /// of its hand (it is dropped), so a repaired or shaped replacement can take it; stowed items stay stowed.
    /// </summary>
    public HoldState HoldStateOf(EquipmentInstance part)
    {
        if (!part.Definition.Handheld)
            return HoldState.NotHandheld;

        if (HolderOf(part) is { } holder)
        {
            if (holder.Definition.ExtraHands <= 0)
                return HoldState.Stowed;
            if (!part.IsOperational)
                return HoldState.Unheld;
            // A limb holds its first ExtraHands operational items, in Damage Order.
            int limbRank = RankAmong(part, e => e.IsOperational && HolderOf(e) == holder);
            return limbRank < holder.Definition.ExtraHands ? HoldState.Limb : HoldState.Unheld;
        }

        if (!part.IsOperational)
            return HoldState.Unheld;
        // The Crest's own hands hold the first operational unassigned handhelds in Damage Order.
        int rank = RankAmong(part, e => e.Definition.Handheld && e.IsOperational && HolderOf(e) is null);
        return rank < _rules.BaseHands ? HoldState.OwnHand : HoldState.Unheld;
    }

    /// <summary>The part's position (0-based) among installed parts matching <paramref name="group"/>, in Damage Order.</summary>
    private int RankAmong(EquipmentInstance part, Func<EquipmentInstance, bool> group)
    {
        int rank = 0;
        foreach (var e in _installed)
        {
            if (ReferenceEquals(e, part))
                return rank;
            if (group(e))
                rank++;
        }
        return int.MaxValue;
    }

    /// <summary>The limb or harness holding a part, if any.</summary>
    public EquipmentInstance? HolderOf(EquipmentInstance part) =>
        _holders.TryGetValue(part.InstanceId, out var id) ? FindInstalled(id) : null;

    /// <summary>
    /// Whether a part works right now: operational and, if handheld, held in a working hand (an own hand, or an
    /// operational limb). Stowed and unheld handhelds don't work.
    /// </summary>
    public bool IsActive(EquipmentInstance part)
    {
        if (!part.IsOperational)
            return false;
        return HoldStateOf(part) switch
        {
            HoldState.NotHandheld or HoldState.OwnHand => true,
            HoldState.Limb => HolderOf(part)?.IsOperational == true,
            _ => false
        };
    }

    /// <summary>
    /// Assigns a handheld to a limb or harness (<paramref name="holderId"/>), or back to the Crest's own hands
    /// (null). Fails if the part isn't handheld, the holder isn't a limb or harness, or the holder is full.
    /// </summary>
    public bool SetHolder(string heldId, string? holderId)
    {
        if (FindInstalled(heldId) is not { Definition.Handheld: true })
            return false;

        if (holderId is null)
        {
            bool removed = _holders.Remove(heldId);
            if (removed)
                _structureVersion++;
            return true;
        }

        if (FindInstalled(holderId) is not { } holder || holderId == heldId)
            return false;
        int capacity = holder.Definition.ExtraHands > 0 ? holder.Definition.ExtraHands : holder.Definition.StowSlots;
        if (capacity <= 0)
            return false;
        int used = _installed.Count(e => e.InstanceId != heldId && e.IsOperational && _holders.GetValueOrDefault(e.InstanceId) == holderId);
        if (used >= capacity)
            return false;

        _holders[heldId] = holderId;
        _structureVersion++;
        return true;
    }

    /// <summary>
    /// Harness swap: the stowed item in <paramref name="harnessId"/> takes the place of <paramref name="heldId"/>
    /// (an own hand or a limb), which goes into the harness.
    /// </summary>
    public bool Swap(string harnessId, string heldId)
    {
        var stowed = _holders.FirstOrDefault(kv => kv.Value == harnessId).Key;
        if (stowed is null || FindInstalled(heldId) is not { } held)
            return false;
        var state = HoldStateOf(held);
        if (state is not (HoldState.OwnHand or HoldState.Limb))
            return false;

        string? previousHolder = _holders.GetValueOrDefault(heldId);
        _holders[heldId] = harnessId;
        if (previousHolder is null)
            _holders.Remove(stowed);
        else
            _holders[stowed] = previousHolder;
        _structureVersion++;
        return true;
    }

    /// <summary>Whether a handheld could be put in a hand right now: a free own hand, or a working limb with room.</summary>
    public bool HasFreeHand()
    {
        int inOwnHands = _installed.Count(e => e.Definition.Handheld && e.IsOperational && HolderOf(e) is null);
        return inOwnHands < _rules.BaseHands || FreeLimb() is not null;
    }

    /// <summary>Puts a handheld in a free hand: it stays in an own hand if one is free, otherwise goes to a working limb with room.</summary>
    public bool HoldInFreeHand(EquipmentInstance part)
    {
        if (HoldStateOf(part) is HoldState.OwnHand or HoldState.Limb)
            return true;
        return FreeLimb() is { } limb && SetHolder(part.InstanceId, limb.InstanceId);
    }

    private EquipmentInstance? FreeLimb() =>
        _installed.FirstOrDefault(e => e.IsOperational && e.Definition.ExtraHands > 0 &&
                                       _installed.Count(x => x.IsOperational && HolderOf(x) == e) < e.Definition.ExtraHands);

    /// <summary>The handheld stowed in a harness, if any.</summary>
    public EquipmentInstance? StowedIn(EquipmentInstance harness) =>
        _holders.FirstOrDefault(kv => kv.Value == harness.InstanceId).Key is { } id ? FindInstalled(id) : null;

    /// <summary>Removes every conjured part (they last only until the end of a fight).</summary>
    public void RemoveConjured()
    {
        foreach (var part in _installed.Where(e => e.IsConjured).ToList())
            Uninstall(part.InstanceId);
    }

    private EquipmentInstance? FindInstalled(string instanceId) =>
        _installed.FirstOrDefault(e => e.InstanceId == instanceId);

    /// <summary>
    /// Installs a component. By default it is placed ahead of any Core(s) at the end of the Damage Order,
    /// so the Core keeps its recommended last position.
    /// </summary>
    /// <returns>False (and nothing changes) if a component with the same instance id is already installed.</returns>
    public bool Install(EquipmentInstance instance)
    {
        if (_installed.Any(e => e.InstanceId == instance.InstanceId))
            return false;

        int insertAt = _installed.Count;
        if (!instance.Definition.IsCore)
        {
            while (insertAt > 0 && _installed[insertAt - 1].Definition.IsCore)
                insertAt--;
        }

        _installed.Insert(insertAt, instance);
        _structureVersion++;
        return true;
    }

    // ---- Cargo -------------------------------------------------------------------------------

    /// <summary>What the Workshop loads before each encounter.</summary>
    public IReadOnlyList<CargoEntry> CargoManifest => _cargoManifest.Select(m => m.Entry).ToList();

    /// <summary>Fast-iron currently carried in cargo.</summary>
    public int CarriedFastIron { get; private set; }

    /// <summary>Consumables currently carried, by id.</summary>
    public IReadOnlyDictionary<string, int> CarriedConsumables =>
        _consumables.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

    public int GetConsumableCount(string consumableId) =>
        _consumables.TryGetValue(consumableId, out var c) ? c.Count : 0;

    /// <summary>Replaces the manifest (resolving consumables against the catalog) and restocks to it.</summary>
    public void SetCargoManifest(IEnumerable<CargoEntry> entries, GameCatalog catalog)
    {
        _cargoManifest.Clear();
        foreach (var entry in entries.Where(e => e.Count > 0))
        {
            ConsumableDefinition? def = null;
            if (entry.Kind == CargoKind.Consumable &&
                (entry.ItemId is null || !catalog.Consumables.TryGetValue(entry.ItemId, out def)))
                continue;

            _cargoManifest.Add((entry, def));
        }

        RestockCargo();
    }

    /// <summary>Refills cargo to the manifest (the Workshop's supply is free).</summary>
    public void RestockCargo()
    {
        CarriedFastIron = 0;
        _consumables.Clear();
        foreach (var (entry, def) in _cargoManifest)
        {
            if (entry.Kind == CargoKind.FastIron)
                CarriedFastIron += entry.Count;
            else if (def is not null)
                _consumables[def.Id] = (def, GetConsumableCount(def.Id) + entry.Count);
        }

        _structureVersion++;
    }

    public int CargoUnitsUsed(RulesConfig rules) =>
        CarriedFastIron * rules.FastIronCargoSize + _consumables.Values.Sum(c => c.Def.CargoSize * c.Count);

    public int CargoWeight(RulesConfig rules) =>
        CarriedFastIron * rules.FastIronWeight + _consumables.Values.Sum(c => c.Def.Weight * c.Count);

    /// <summary>Sets the carried cargo to a saved state (fast-iron and consumables), e.g. when resuming a mission.</summary>
    public void RestoreCargo(int carriedFastIron, IReadOnlyDictionary<string, int> consumables, GameCatalog catalog)
    {
        CarriedFastIron = Math.Max(0, carriedFastIron);
        _consumables.Clear();
        foreach (var (id, count) in consumables)
            if (count > 0 && catalog.Consumables.TryGetValue(id, out var def))
                _consumables[id] = (def, count);
        _structureVersion++;
    }

    public bool TryTakeCarriedFastIron(int amount)
    {
        if (amount <= 0)
            return true;
        if (CarriedFastIron < amount)
            return false;

        CarriedFastIron -= amount;
        _structureVersion++;
        return true;
    }

    public bool TryTakeConsumable(string consumableId)
    {
        if (!_consumables.TryGetValue(consumableId, out var c) || c.Count <= 0)
            return false;

        if (c.Count == 1)
            _consumables.Remove(consumableId);
        else
            _consumables[consumableId] = (c.Def, c.Count - 1);

        _structureVersion++;
        return true;
    }

    /// <summary>Adds a consumable to cargo if there is room in operational cargo components.</summary>
    public bool TryAddConsumable(ConsumableDefinition def, RulesConfig rules)
    {
        if (CargoUnitsUsed(rules) + def.CargoSize > GetStats(rules).CargoCapacity)
            return false;

        _consumables[def.Id] = (def, GetConsumableCount(def.Id) + 1);
        _structureVersion++;
        return true;
    }

    /// <summary>
    /// Installs a component at the very end of the Damage Order (after the Core). Components shaped in the field
    /// always go here.
    /// </summary>
    public void InstallAtEnd(EquipmentInstance instance)
    {
        if (_installed.Any(e => e.InstanceId == instance.InstanceId))
            return;

        _installed.Add(instance);
        _structureVersion++;
    }

    public void AddReactionRule(ReactionRule rule) => _reactionRules.Add(rule);

    public bool RemoveReactionRuleAt(int index)
    {
        if (index < 0 || index >= _reactionRules.Count)
            return false;
        _reactionRules.RemoveAt(index);
        return true;
    }

    public bool MoveReactionRule(int index, int delta)
    {
        int target = index + delta;
        if (index < 0 || index >= _reactionRules.Count || target < 0 || target >= _reactionRules.Count)
            return false;
        (_reactionRules[index], _reactionRules[target]) = (_reactionRules[target], _reactionRules[index]);
        return true;
    }

    public EquipmentInstance? Uninstall(string instanceId)
    {
        var item = _installed.FirstOrDefault(e => e.InstanceId == instanceId);
        if (item is null)
            return null;

        _installed.Remove(item);
        // A removed part no longer holds or is held; what a removed limb held goes back to the own hands.
        _holders.Remove(instanceId);
        foreach (var held in _holders.Where(kv => kv.Value == instanceId).Select(kv => kv.Key).ToList())
            _holders.Remove(held);
        _structureVersion++;
        return item;
    }

    /// <summary>Moves a component within the Damage Order by <paramref name="delta"/> positions (negative = earlier).</summary>
    public bool MoveInDamageOrder(string instanceId, int delta)
    {
        int index = _installed.FindIndex(e => e.InstanceId == instanceId);
        if (index < 0 || delta == 0)
            return false;

        int newIndex = Math.Clamp(index + delta, 0, _installed.Count - 1);
        if (newIndex == index)
            return false;

        var item = _installed[index];
        _installed.RemoveAt(index);
        _installed.Insert(newIndex, item);
        _structureVersion++; // the own hands hold handhelds in Damage Order, so this can change what works
        return true;
    }

    public bool TransferComponentTo(string instanceId, CrestLoadout destination)
    {
        if (ReferenceEquals(this, destination) || destination.FindById(instanceId) is not null)
            return false;

        var removed = Uninstall(instanceId);
        if (removed is null)
            return false;

        return destination.Install(removed);
    }

    /// <summary>
    /// Cached <see cref="LoadoutRules.ComputeStats"/>. Recomputed only when components are installed or removed,
    /// or one of them becomes operational/disabled (durability and ammo changes alone do not affect stats).
    /// </summary>
    public CrestStats GetStats(RulesConfig? rules = null)
    {
        var r = rules ?? _rules;
        int operational = 0;
        foreach (var e in _installed)
            operational += e.OperationalVersion;

        if (_statsCache is { } c && c.Structure == _structureVersion && c.Operational == operational && ReferenceEquals(c.Rules, r))
            return c.Stats;

        var stats = LoadoutRules.ComputeStats(this, r);
        _statsCache = (_structureVersion, operational, r, stats);
        return stats;
    }

    /// <summary>An installed component by instance id (or the <see cref="Pilot"/>, for core spells).</summary>
    public EquipmentInstance? FindById(string instanceId) =>
        instanceId == PilotInstanceId ? Pilot : FindInstalled(instanceId);

    /// <summary>Every source of an action that works right now: active components, then the pilot for core spells.</summary>
    public IEnumerable<EquipmentInstance> ProvidersFor(string actionId)
    {
        foreach (var e in _installed)
            if (e.Definition.GrantedActionIds.Contains(actionId) && IsActive(e))
                yield return e;
        if (_rules.CoreSpellIds.Contains(actionId))
            yield return Pilot;
    }

    public EquipmentInstance? FindOperationalProviderForAction(string actionId) => ProvidersFor(actionId).FirstOrDefault();

    /// <summary>Every action the Crest can use right now: its active components' actions and the core spells.</summary>
    public IEnumerable<string> GetOperationalActionIds() =>
        _installed
            .Where(IsActive)
            .SelectMany(e => e.Definition.GrantedActionIds)
            .Concat(_rules.CoreSpellIds)
            .Distinct();

    public void RepairAll()
    {
        foreach (var item in _installed)
        {
            item.Repair();
            item.RefillAmmo();
        }

        RestockCargo();
    }
}
