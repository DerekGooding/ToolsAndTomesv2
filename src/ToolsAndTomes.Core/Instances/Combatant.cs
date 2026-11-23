using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Instances;

public sealed class Combatant
{
    public const string ActionWitReservationKey = "active_action";
    public const string AegisWitReservationKey = "active_aegis";

    private readonly Dictionary<string, int> _cooldowns = [];

    public string Id { get; }
    public string Name { get; }
    public Faction Faction { get; }
    public EnemyArchetype? Archetype { get; }
    public double PreferredRange { get; }

    public CrestLoadout Loadout { get; }
    public ResourceState Resources { get; }

    public Vec2 Position { get; set; }
    public Vec2? MoveDestination { get; set; }

    public ActionState State { get; set; } = ActionState.Idle;
    public ActiveAction? CurrentAction { get; set; }
    public CombatCommand? QueuedCommand { get; set; }

    public int InterruptedTicksRemaining { get; set; }
    public int EvadeTicksRemaining { get; set; }

    public int AegisBlocksRemaining { get; set; }
    public int AegisTicksRemaining { get; set; }

    public IReadOnlyDictionary<string, int> Cooldowns => _cooldowns;
    public CrestStats Stats => LoadoutRules.ComputeStats(Loadout);
    public bool IsDefeated => !Stats.IsCoreOperational;
    public bool IsEvading => EvadeTicksRemaining > 0;
    public bool HasActiveAegis => AegisBlocksRemaining > 0 && AegisTicksRemaining > 0;

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

    public int GetCooldown(string actionId) =>
        _cooldowns.TryGetValue(actionId, out var ticks) ? ticks : 0;

    public void SetCooldown(string actionId, int ticks)
    {
        if (ticks > 0)
            _cooldowns[actionId] = ticks;
    }

    public void TickCooldowns()
    {
        if (_cooldowns.Count == 0)
            return;

        var keys = _cooldowns.Keys.ToList();
        foreach (var key in keys)
        {
            _cooldowns[key]--;
            if (_cooldowns[key] <= 0)
                _cooldowns.Remove(key);
        }
    }

    public void ActivateAegis(int blocks, int durationTicks, int witReserve)
    {
        AegisBlocksRemaining = blocks;
        AegisTicksRemaining = durationTicks;
        Resources.ReserveWit(AegisWitReservationKey, witReserve);
    }

    public void ClearAegis()
    {
        AegisBlocksRemaining = 0;
        AegisTicksRemaining = 0;
        Resources.ReleaseWit(AegisWitReservationKey);
    }
}
