using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using ToolsAndTomes.Core.Workshop;

namespace ToolsAndTomes.Core.Campaign;

public enum MissionPhase
{
    /// <summary>Ready to fight (or fighting) the current encounter.</summary>
    Encounter,

    /// <summary>Resting in the field between encounters.</summary>
    Rest,

    Completed,
    Failed,
    Withdrawn
}

public sealed record RestResult(bool Succeeded, string Message)
{
    public static RestResult Ok(string message) => new(true, message);
    public static RestResult Fail(string message) => new(false, message);
}

/// <summary>
/// One play-through of a <see cref="MissionDefinition"/>: encounter → rest → encounter → … The same Crest
/// (and its damage, ammo, cargo, Motes, and fast-iron) carries through. Rewards are banked as each encounter
/// is won, so a failed or abandoned mission keeps what was earned; finishing grants the completion bonus.
/// </summary>
/// <remarks>
/// Rests in the field have a time budget spent mostly on waiting for Motes. Square Summon, repairs, and shaping
/// are assumed to happen between Mote generations, so they cost fast-iron and Motes but no extra time.
/// At the table: during a rest, advance time one Mote interval at a time; between intervals, summon, repair, and shape.
/// </remarks>
public sealed class MissionRun
{
    private readonly List<MaterialCount> _rewardsEarned = [];
    private readonly List<CombatEvent> _restLog = [];
    private int _restOps;

    public MissionDefinition Mission { get; }
    public GameCatalog Catalog { get; }
    public CrestLoadout Loadout { get; }

    /// <summary>The player's Crest for the whole mission.</summary>
    public Combatant Crest { get; }

    public int BaseSeed { get; }
    public int StageIndex { get; private set; }
    public MissionPhase Phase { get; private set; } = MissionPhase.Encounter;
    public CombatSimulation? ActiveSimulation { get; private set; }
    public int RestBeatsRemaining { get; private set; }

    /// <summary>Samples banked so far this mission (already granted to the Workshop by the caller).</summary>
    public IReadOnlyList<MaterialCount> RewardsEarned => _rewardsEarned;

    /// <summary>What happened during rests (Motes gained, summons, repairs, shaping).</summary>
    public IReadOnlyList<CombatEvent> RestLog => _restLog;

    public EncounterDefinition CurrentEncounter => Catalog.Encounters[Mission.EncounterIds[StageIndex]];
    public int CurrentSeed => BaseSeed + StageIndex * 7919;
    public bool IsFinalEncounter => StageIndex == Mission.EncounterIds.Count - 1;
    public bool IsOver => Phase is MissionPhase.Completed or MissionPhase.Failed or MissionPhase.Withdrawn;

    private MissionRun(MissionDefinition mission, CrestLoadout loadout, GameCatalog catalog, int seed, IReadOnlySet<string>? knownBlueprints)
    {
        Mission = mission;
        Catalog = catalog;
        Loadout = loadout;
        BaseSeed = seed;

        var first = catalog.Encounters[mission.EncounterIds[0]];
        Crest = new Combatant(EncounterFactory.PlayerCombatantId, loadout.Name, Faction.Player, loadout,
            first.PlayerStart, first.PlayerStartingMotes)
        {
            Rules = catalog.Rules,
            KnownBlueprints = knownBlueprints
        };
    }

    /// <summary>Starts a mission: the Workshop repairs and restocks the Crest one last time.</summary>
    public static MissionRun Start(
        MissionDefinition mission,
        CrestLoadout loadout,
        GameCatalog catalog,
        int seed,
        IReadOnlySet<string>? knownBlueprints = null)
    {
        loadout.RepairAll();
        return new MissionRun(mission, loadout, catalog, seed, knownBlueprints);
    }

    public static bool IsUnlocked(MissionDefinition mission, WorkshopState workshop) =>
        mission.RequiredResearch.All(workshop.IsResearched);

    // ---- Encounters ------------------------------------------------------------------------

    public CombatSimulation BeginEncounter()
    {
        if (Phase != MissionPhase.Encounter)
            throw new InvalidOperationException($"Cannot begin an encounter during {Phase}.");

        ActiveSimulation = EncounterFactory.CreateEncounter(CurrentEncounter, Crest, CurrentSeed, Catalog);
        return ActiveSimulation;
    }

    /// <summary>
    /// Settles the finished encounter. Victory banks the encounter's rewards (plus the completion bonus after the
    /// last one) and moves to a rest or completes the mission; defeat fails it. Returns newly banked samples,
    /// which the caller grants to the Workshop.
    /// </summary>
    public IReadOnlyList<MaterialCount> FinishEncounter()
    {
        if (Phase != MissionPhase.Encounter || ActiveSimulation is null)
            throw new InvalidOperationException("No encounter to finish.");

        var sim = ActiveSimulation;
        if (sim.Outcome == EncounterOutcome.InProgress)
            throw new InvalidOperationException("The encounter is still in progress.");

        ActiveSimulation = null;
        if (sim.Outcome == EncounterOutcome.PlayerDefeat)
        {
            Phase = MissionPhase.Failed;
            return [];
        }

        var granted = RewardRoller.Roll(CurrentEncounter, CurrentSeed).ToList();
        if (IsFinalEncounter)
        {
            granted.AddRange(Mission.CompletionRewards);
            Phase = MissionPhase.Completed;
        }
        else
        {
            Phase = MissionPhase.Rest;
            RestBeatsRemaining = Mission.RestBeats;
        }

        _rewardsEarned.AddRange(granted);
        return granted;
    }

    /// <summary>Leaves the mission (during a rest, or abandoning a fight in progress), keeping banked rewards.</summary>
    public void Withdraw()
    {
        if (IsOver)
            return;

        ActiveSimulation = null;
        Phase = MissionPhase.Withdrawn;
    }

    // ---- Rest in the field -----------------------------------------------------------------

    /// <summary>Beats needed to generate the next Mote.</summary>
    public int MoteIntervalBeats => Crest.Stats.MoteGenerationIntervalBeats;

    /// <summary>Spends one Mote interval of rest time to gain a Mote (wasted if storage is full).</summary>
    public RestResult Wait()
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");

        int interval = MoteIntervalBeats;
        if (RestBeatsRemaining < interval)
            return RestResult.Fail($"Not enough rest time left (needs {interval} beats).");

        RestBeatsRemaining -= interval;
        var stats = Crest.Stats;
        if (Crest.Resources.CurrentMotes >= stats.MaxMotes)
            return Log(RestResult.Ok($"Rested {interval} beats; Mote storage already full ({stats.MaxMotes})."));

        Crest.Resources.AddMotes(1, stats.MaxMotes);
        return Log(RestResult.Ok($"Rested {interval} beats: +1 Mote ({Crest.Resources.CurrentMotes}/{stats.MaxMotes})."));
    }

    /// <summary>Casts Square Summon between Mote generations: spends its Mote cost for fast-iron.</summary>
    public RestResult SquareSummon()
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");

        var summon = Crest.Loadout.GetOperationalActionIds()
            .Select(Catalog.GetAction)
            .FirstOrDefault(a => a.FastIronProduced > 0);
        if (summon is null)
            return RestResult.Fail("No operational component can cast Square Summon.");
        if (summon.RequiresSpellbook && !Crest.Stats.HasSpellbook)
            return RestResult.Fail($"{summon.Name} requires an operational Spellbook.");
        if (summon.MoteCost > Crest.Stats.MaxMotes)
            return RestResult.Fail($"{summon.Name} needs {summon.MoteCost} Motes; this Crest can only store {Crest.Stats.MaxMotes}.");
        if (!Crest.Resources.TrySpendMotes(summon.MoteCost))
            return RestResult.Fail($"{summon.Name} needs {summon.MoteCost} Motes (have {Crest.Resources.CurrentMotes}).");

        Crest.Resources.AddFastIron(summon.FastIronProduced);
        return Log(RestResult.Ok($"{summon.Name}: {summon.MoteCost} Motes → {summon.FastIronProduced} fast-iron ({Crest.TotalFastIron} on hand)."));
    }

    /// <summary>Restores one point of durability to a component for fast-iron (a disabled part comes back online).</summary>
    public RestResult Repair(string componentInstanceId)
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");

        var part = Crest.Loadout.FindById(componentInstanceId);
        if (part is null)
            return RestResult.Fail("Unknown component.");
        if (part.CurrentDurability >= part.Definition.MaxDurability)
            return RestResult.Fail($"{part.Definition.Name} is undamaged.");

        int cost = Crest.Rules.FastIronPerRepairPoint;
        if (!Crest.TrySpendFastIron(cost))
            return RestResult.Fail($"Repair needs {cost} fast-iron (have {Crest.TotalFastIron}).");

        part.RestoreDurability(1);
        return Log(RestResult.Ok($"Repaired {part.Definition.Name} ({part.CurrentDurability}/{part.Definition.MaxDurability}) for {cost} fast-iron."));
    }

    /// <summary>Shapes fast-iron into a component, consumable, or ammo (needs a Shaping spell on the Crest).</summary>
    public RestResult Shape(ShapingOrder order, string? ownComponentInstanceId = null)
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");

        var shaping = Crest.Loadout.GetOperationalActionIds().Select(Catalog.GetAction).FirstOrDefault(a => a.IsShaping);
        if (shaping is null)
            return RestResult.Fail("No operational component can cast a Shaping spell.");
        if (shaping.RequiresSpellbook && !Crest.Stats.HasSpellbook)
            return RestResult.Fail($"{shaping.Name} requires an operational Spellbook.");

        var command = new UseActionCommand(Crest.Id, shaping.Id, OwnComponentInstanceId: ownComponentInstanceId, Shaping: order);
        var (check, plan) = ShapingRules.Validate(Crest, shaping, command, Catalog);
        if (!check.IsValid || plan is null)
            return RestResult.Fail(check.FailureReason ?? "Cannot shape that.");

        Crest.TrySpendFastIron(plan.FastIronCost);
        var events = new List<CombatEvent>();
        ShapingRules.Apply(Crest, plan, $"{Crest.Id}_rest{StageIndex}_{++_restOps}", -1, events);
        _restLog.AddRange(events);
        return RestResult.Ok(events.LastOrDefault()?.Message ?? "Shaped.");
    }

    /// <summary>Uses a cargo item that works on the Crest itself (e.g. an Ammo Pack into a weapon).</summary>
    public RestResult UseCargoItem(string actionId, string ownComponentInstanceId)
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");
        if (!Catalog.Actions.TryGetValue(actionId, out var action) || !action.RefillsAmmo || action.ConsumableItemId is null)
            return RestResult.Fail("That item cannot be used during a rest.");
        if (Crest.Loadout.FindOperationalProviderForAction(action.Id) is null)
            return RestResult.Fail("No operational cargo component holds it.");

        var command = new UseActionCommand(Crest.Id, action.Id, OwnComponentInstanceId: ownComponentInstanceId);
        var (check, plan) = ShapingRules.Validate(Crest, action, command, Catalog);
        if (!check.IsValid || plan?.AmmoTarget is not { } weapon)
            return RestResult.Fail(check.FailureReason ?? "Cannot use that.");
        if (!Crest.Loadout.TryTakeConsumable(action.ConsumableItemId))
            return RestResult.Fail("None left in cargo.");

        weapon.RefillAmmo();
        return Log(RestResult.Ok($"{action.Name}: {weapon.Definition.Name} restocked ({weapon.CurrentAmmo}/{weapon.Definition.MaxAmmo})."));
    }

    /// <summary>Ends the rest (any remaining time is lost) and readies the next encounter.</summary>
    public void ContinueToNextEncounter()
    {
        if (Phase != MissionPhase.Rest)
            throw new InvalidOperationException("Not resting.");

        StageIndex++;
        RestBeatsRemaining = 0;
        Phase = MissionPhase.Encounter;
    }

    private RestResult Log(RestResult result)
    {
        _restLog.Add(new CombatEvent(-1, CombatEventType.FieldShaped, Crest.Id, result.Message));
        return result;
    }
}
