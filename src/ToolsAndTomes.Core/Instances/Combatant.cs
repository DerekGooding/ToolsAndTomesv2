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

    /// <summary>Position at the start of the most recently resolved beat (for presentation interpolation).</summary>
    public Vec2 PreviousPosition { get; set; }
    public Vec2? MoveDestination { get; set; }

    public ActionState State { get; set; } = ActionState.Idle;
    public ActiveAction? CurrentAction { get; set; }

    /// <summary>Buffered command applied at the next beat boundary (or once the Crest is free to act).</summary>
    public CombatCommand? QueuedCommand { get; set; }

    /// <summary>Reaction declared during the current beat; resolves at the end of this beat before impacts.</summary>
    public UseActionCommand? PendingReaction { get; set; }

    /// <summary>A cancel request buffered for the next beat boundary.</summary>
    public bool PendingCancel { get; set; }

    public int InterruptedBeatsRemaining { get; set; }
    public int InterruptedTotalBeats { get; set; }
    public int EvadeBeatsRemaining { get; set; }

    public int AegisBlocksRemaining { get; set; }
    public int AegisBeatsRemaining { get; set; }

    /// <summary>Beats remaining before each action may start again.</summary>
    public IReadOnlyDictionary<string, int> Cooldowns => _cooldowns;
    public CrestStats Stats => LoadoutRules.ComputeStats(Loadout);
    public bool IsDefeated => !Stats.IsCoreOperational;
    public bool IsEvading => EvadeBeatsRemaining > 0;
    public bool HasActiveAegis => AegisBlocksRemaining > 0 && AegisBeatsRemaining > 0;

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
        PreviousPosition = startPosition;
        Resources = new ResourceState(startingMotes);
        Archetype = archetype;
        PreferredRange = preferredRange;
    }

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

        var keys = _cooldowns.Keys.ToList();
        foreach (var key in keys)
        {
            _cooldowns[key]--;
            if (_cooldowns[key] <= 0)
                _cooldowns.Remove(key);
        }
    }

    public void ActivateAegis(int blocks, int durationBeats, int witReserve)
    {
        AegisBlocksRemaining = blocks;
        AegisBeatsRemaining = durationBeats;
        Resources.ReserveWit(AegisWitReservationKey, witReserve);
    }

    public void ClearAegis()
    {
        AegisBlocksRemaining = 0;
        AegisBeatsRemaining = 0;
        Resources.ReleaseWit(AegisWitReservationKey);
    }
}
