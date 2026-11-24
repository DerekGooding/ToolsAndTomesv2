using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Instances;

public sealed class Combatant
{
    public const string AegisWitReservationKey = "active_aegis";
    public const string MovementWitReservationKey = "movement";

    private readonly Dictionary<string, int> _cooldowns = [];
    private readonly List<ActiveAction> _actions = [];
    private readonly List<CombatCommand> _queuedCommands = [];
    private readonly List<UseActionCommand> _pendingReactions = [];
    private readonly Dictionary<string, (int Remaining, int Total)> _componentLockouts = [];
    private int _nextActionId = 1;

    public string Id { get; }
    public string Name { get; }
    public Faction Faction { get; }
    public EnemyArchetype? Archetype { get; }
    public double PreferredRange { get; }

    public CrestLoadout Loadout { get; }
    public ResourceState Resources { get; }

    public Vec2 Position { get; set; }

    /// <summary>Current movement order, carried out by a locomotion component.</summary>
    public MovementOrder? Movement { get; set; }

    /// <summary>All actions currently running on this Crest (at most one per component).</summary>
    public IReadOnlyList<ActiveAction> ActionsInMotion => _actions;

    /// <summary>Commands buffered until the next beat boundary (or until their component is free).</summary>
    public IReadOnlyList<CombatCommand> QueuedCommands => _queuedCommands;
    public bool HasQueuedCommands => _queuedCommands.Count > 0;

    /// <summary>Reactions declared during the current beat; they resolve at the end of this beat before impacts.</summary>
    public IReadOnlyList<UseActionCommand> PendingReactions => _pendingReactions;

    /// <summary>A cancel request buffered for the next beat boundary.</summary>
    public bool PendingCancel { get; set; }

    /// <summary>Components locked out by interruption, with beats remaining and total penalty.</summary>
    public IReadOnlyDictionary<string, (int Remaining, int Total)> ComponentLockouts => _componentLockouts;

    public int EvadeBeatsRemaining { get; set; }
    public int AegisBlocksRemaining { get; set; }
    public int AegisBeatsRemaining { get; set; }

    /// <summary>Beats remaining before each action may start again.</summary>
    public IReadOnlyDictionary<string, int> Cooldowns => _cooldowns;
    /// <summary>Global rules this Crest is evaluated under (set by the simulation from its catalog).</summary>
    public RulesConfig Rules { get; set; } = RulesConfig.Default;

    public CrestStats Stats => Loadout.GetStats(Rules);

    /// <summary>Blueprints this Crest may shape in the field (null = any component in the catalog).</summary>
    public IReadOnlySet<string>? KnownBlueprints { get; set; }

    /// <summary>Fast-iron available for shaping: loose (from Square Summon) plus carried in cargo.</summary>
    public int TotalFastIron => Resources.FastIron + Loadout.CarriedFastIron;

    /// <summary>Spends fast-iron, loose first, then from cargo.</summary>
    public bool TrySpendFastIron(int amount)
    {
        if (amount <= 0)
            return true;
        if (TotalFastIron < amount)
            return false;

        int fromLoose = Math.Min(amount, Resources.FastIron);
        Resources.TrySpendFastIron(fromLoose);
        return Loadout.TryTakeCarriedFastIron(amount - fromLoose);
    }
    public bool IsDefeated => !Stats.IsCoreOperational;
    public bool IsEvading => EvadeBeatsRemaining > 0;
    public bool HasActiveAegis => AegisBlocksRemaining > 0 && AegisBeatsRemaining > 0;

    /// <summary>
    /// Summary state derived from everything the Crest is doing, most urgent first:
    /// Executing &gt; Windup &gt; Recovery &gt; Moving &gt; Interrupted &gt; Idle.
    /// </summary>
    public ActionState State
    {
        get
        {
            if (_actions.Any(a => a.Phase == ActionState.Executing)) return ActionState.Executing;
            if (_actions.Any(a => a.Phase == ActionState.Windup)) return ActionState.Windup;
            if (_actions.Any(a => a.Phase == ActionState.Recovery)) return ActionState.Recovery;
            if (Movement is not null || _actions.Any(a => a.Phase == ActionState.Moving)) return ActionState.Moving;
            if (_componentLockouts.Count > 0) return ActionState.Interrupted;
            return ActionState.Idle;
        }
    }

    /// <summary>The most advanced action in motion (for summaries); null when none.</summary>
    public ActiveAction? CurrentAction =>
        _actions.FirstOrDefault(a => a.Phase == ActionState.Executing)
        ?? _actions.FirstOrDefault(a => a.Phase == ActionState.Windup)
        ?? _actions.FirstOrDefault(a => a.Phase == ActionState.Recovery)
        ?? _actions.FirstOrDefault();

    public Combatant(
        string id,
        string name,
        Faction faction,
        CrestLoadout loadout,
        Vec2 startPosition,
        int startingMotes = 1,
        EnemyArchetype? archetype = null,
        double preferredRange = 3.0)
    {
        Id = id;
        Name = name;
        Faction = faction;
        Loadout = loadout;
        Position = startPosition;
        Resources = new ResourceState(startingMotes);
        Archetype = archetype;
        PreferredRange = preferredRange;
    }

    // ---- Components & actions ---------------------------------------------------------------

    public bool IsComponentBusy(string instanceId) =>
        _actions.Any(a => a.SourceEquipmentInstanceId == instanceId) ||
        _componentLockouts.ContainsKey(instanceId) ||
        Movement?.ProviderInstanceId == instanceId;

    /// <summary>An operational component granting the action that is not running anything and not locked out.</summary>
    public EquipmentInstance? FindFreeProvider(string actionId) =>
        Loadout.Installed.FirstOrDefault(e =>
            e.IsOperational &&
            e.Definition.GrantedActionIds.Contains(actionId) &&
            !IsComponentBusy(e.InstanceId));

    /// <summary>The operational locomotion component used for walking (busy movement is allowed: it is redirected).</summary>
    public EquipmentInstance? FindLocomotionProvider() =>
        Loadout.Installed.FirstOrDefault(e =>
            e.IsOperational && e.Definition.IsLocomotion && !_componentLockouts.ContainsKey(e.InstanceId));

    public ActiveAction AddAction(ActionDefinition def, string providerInstanceId, string? targetActorId, Vec2? targetPoint)
    {
        var act = new ActiveAction(_nextActionId++, def, providerInstanceId, targetActorId, targetPoint);
        _actions.Add(act);
        return act;
    }

    /// <summary>
    /// Removes an action and releases any Wit it reserved. An approach walk stops once no action is approaching.
    /// </summary>
    public void RemoveAction(ActiveAction act)
    {
        _actions.Remove(act);
        Resources.ReleaseWit(act.WitReservationKey);
        if (Movement is { ForActionId: not null } && !_actions.Any(a => a.Phase == ActionState.Moving))
            StopMovement();
    }

    /// <summary>
    /// Removes an action cut short (interrupted, canceled, or dropped by Wit collapse). If its costs were already
    /// committed, its cooldown still applies.
    /// </summary>
    public void AbortAction(ActiveAction act)
    {
        RemoveAction(act);
        if (act.CostsCommitted)
            SetCooldown(act.Definition.Id, act.Definition.CooldownBeats);
    }

    public void StopMovement()
    {
        Movement = null;
        Resources.ReleaseWit(MovementWitReservationKey);
    }

    public void LockOutComponent(string instanceId, int beats)
    {
        if (beats > 0)
            _componentLockouts[instanceId] = (beats, beats);
    }

    public void TickLockouts()
    {
        foreach (var key in _componentLockouts.Keys.ToList())
        {
            var (remaining, total) = _componentLockouts[key];
            if (remaining <= 1)
                _componentLockouts.Remove(key);
            else
                _componentLockouts[key] = (remaining - 1, total);
        }
    }

    /// <summary>
    /// Wit collapse: if available Wit has fallen below zero (e.g. a Wit-granting component was destroyed),
    /// drop Wit commitments newest-first — actions, then Aegis, then paid movement — until the budget balances.
    /// Returns the names of what was dropped.
    /// </summary>
    public List<string> EnforceWitBudget()
    {
        var dropped = new List<string>();
        int Available() => Stats.AvailableWit(Resources.ReservedWit);

        // Only actions currently holding Wit (winding up or in their impact window) free anything when dropped.
        foreach (var act in _actions
                     .Where(a => a.Phase is ActionState.Windup or ActionState.Executing && Resources.GetReservation(a.WitReservationKey) > 0)
                     .OrderByDescending(a => a.Id)
                     .ToList())
        {
            if (Available() >= 0)
                return dropped;

            AbortAction(act);
            dropped.Add(act.Definition.Name);
        }

        if (Available() < 0 && HasActiveAegis)
        {
            ClearAegis();
            dropped.Add("Aegis Barrier");
        }

        if (Available() < 0 && Movement is not null)
        {
            StopMovement();
            dropped.Add("movement");
        }

        return dropped;
    }

    /// <summary>Stops everything (used on defeat).</summary>
    public void ClearAllActivity()
    {
        foreach (var act in _actions.ToList())
            RemoveAction(act);

        StopMovement();
        _queuedCommands.Clear();
        _pendingReactions.Clear();
        _componentLockouts.Clear();
        PendingCancel = false;
        ClearAegis();
        Resources.ReleaseAllWit();
    }

    // ---- Command buffering ------------------------------------------------------------------

    /// <summary>
    /// Buffers a command. A newer command replaces a queued one of the same kind
    /// (another move, or the same action).
    /// </summary>
    public void QueueCommand(CombatCommand command)
    {
        _queuedCommands.RemoveAll(existing => existing switch
        {
            MoveCommand when command is MoveCommand => true,
            UseActionCommand u when command is UseActionCommand n => u.ActionId == n.ActionId,
            _ => false
        });
        _queuedCommands.Add(command);
    }

    public void RemoveQueuedCommand(CombatCommand command) => _queuedCommands.Remove(command);
    public void ClearQueuedCommands() => _queuedCommands.Clear();

    public void AddPendingReaction(UseActionCommand reaction)
    {
        _pendingReactions.RemoveAll(r => r.ActionId == reaction.ActionId);
        _pendingReactions.Add(reaction);
    }

    public List<UseActionCommand> TakePendingReactions()
    {
        var list = _pendingReactions.ToList();
        _pendingReactions.Clear();
        return list;
    }

    // ---- Cooldowns & defenses ---------------------------------------------------------------

    public int GetCooldown(string actionId) =>
        _cooldowns.TryGetValue(actionId, out var beats) ? beats : 0;

    public void SetCooldown(string actionId, int beats)
    {
        if (beats > 0)
            _cooldowns[actionId] = beats;
    }

    public void TickCooldowns()
    {
        if (_cooldowns.Count == 0)
            return;

        foreach (var key in _cooldowns.Keys.ToList())
        {
            _cooldowns[key]--;
            if (_cooldowns[key] <= 0)
                _cooldowns.Remove(key);
        }
    }

    /// <summary>
    /// Raises or reinforces the Aegis ward. Wards stack: blocks add up, the longer duration wins, and the larger
    /// Wit commitment is kept (so a cheap Iron Ward never weakens an active Aegis Barrier).
    /// </summary>
    public void ActivateAegis(int blocks, int durationBeats, int witReserve)
    {
        bool active = HasActiveAegis;
        AegisBlocksRemaining = active ? AegisBlocksRemaining + blocks : blocks;
        AegisBeatsRemaining = active ? Math.Max(AegisBeatsRemaining, durationBeats) : durationBeats;

        int held = Resources.GetReservation(AegisWitReservationKey);
        if (witReserve > held)
            Resources.ReserveWit(AegisWitReservationKey, witReserve);
    }

    /// <summary>
    /// Readies a Crest that carries over from a previous encounter (campaign missions): clears all activity,
    /// cooldowns, evasion, and heat, and places it at its start position. Damage, ammo, cargo, Motes, and
    /// fast-iron are kept.
    /// </summary>
    public void PrepareForEncounter(Vec2 startPosition)
    {
        ClearAllActivity();
        _cooldowns.Clear();
        EvadeBeatsRemaining = 0;
        Resources.ResetHeat();
        Position = startPosition;
    }

    public void ClearAegis()
    {
        AegisBlocksRemaining = 0;
        AegisBeatsRemaining = 0;
        Resources.ReleaseWit(AegisWitReservationKey);
    }
}
