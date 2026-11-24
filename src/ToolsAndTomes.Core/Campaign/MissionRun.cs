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
/// (and its damage, ammo, cargo, Motes, and fast-iron) carries through, and so do up to <see cref="MaxAllies"/>
/// allied pilots' Crests. An ally whose Core falls sits out later fights until it is repaired at a rest.
/// Rewards are banked as each encounter is won, so a failed or abandoned mission keeps what was earned;
/// finishing grants the completion bonus.
/// </summary>
/// <remarks>
/// Rests in the field have a time budget spent mostly on waiting for Motes. Square Summon, repairs, and shaping
/// are assumed to happen between Mote generations, so they cost fast-iron and Motes but no extra time.
/// At the table: during a rest, advance time one Mote interval at a time; between intervals, summon, repair, and shape.
/// Every Crest in the lance gains Motes while waiting; each summons, repairs, and shapes with its own Motes and fast-iron.
/// Fast-iron can be handed between Crests at no cost, so one Crest's Square Summon can fuel the lance's repairs.
/// </remarks>
public sealed class MissionRun
{
    /// <summary>The most allied Crests that can join the player (a lance of three).</summary>
    public const int MaxAllies = 2;

    private readonly List<MaterialCount> _rewardsEarned = [];
    private readonly List<CombatEvent> _restLog = [];
    private int _restOps;
    private readonly Dictionary<string, string> _pilotIds = new(StringComparer.Ordinal);

    public MissionDefinition Mission { get; }
    public GameCatalog Catalog { get; }
    public CrestLoadout Loadout { get; }

    /// <summary>The player's Crest for the whole mission.</summary>
    public Combatant Crest { get; }

    /// <summary>The pilot flying an allied Crest, if known.</summary>
    public string? PilotOf(Combatant ally) => _pilotIds.GetValueOrDefault(ally.Id);

    /// <summary>Allied pilots' Crests for the whole mission, in lance order.</summary>
    public IReadOnlyList<Combatant> Allies { get; }

    /// <summary>The player's Crest, then the allies.</summary>
    public IEnumerable<Combatant> Lance => Allies.Prepend(Crest);

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

    private MissionRun(MissionDefinition mission, CrestLoadout loadout, GameCatalog catalog, int seed, IReadOnlySet<string>? knownBlueprints,
        IReadOnlyList<Combatant> allies)
    {
        Allies = allies;
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

    /// <summary>
    /// Starts a mission: the Workshop repairs and restocks every Crest one last time. <paramref name="allies"/> are
    /// allied Crests (see <see cref="EncounterFactory.CreateAlly"/>), at most <see cref="MaxAllies"/>.
    /// </summary>
    public static MissionRun Start(
        MissionDefinition mission,
        CrestLoadout loadout,
        GameCatalog catalog,
        int seed,
        IReadOnlySet<string>? knownBlueprints = null,
        IReadOnlyList<Combatant>? allies = null)
    {
        allies ??= [];
        if (allies.Count > MaxAllies)
            throw new ArgumentException($"A lance holds at most {MaxAllies} allies.", nameof(allies));
        if (allies.Any(a => !AI.AllyAIController.IsAlly(a)) || allies.Select(a => a.Id).Distinct().Count() != allies.Count)
            throw new ArgumentException("Allies must be distinct allied Crests.", nameof(allies));

        loadout.RepairAll();
        foreach (var ally in allies)
            ally.Loadout.RepairAll();
        var run = new MissionRun(mission, loadout, catalog, seed, knownBlueprints, allies);
        foreach (var ally in allies)
            if (catalog.Pilots.Values.FirstOrDefault(p => EncounterFactory.AllyCombatantId(p.Id) == ally.Id) is { } pilot)
                run._pilotIds[ally.Id] = pilot.Id;
        return run;
    }

    // ---- Saving -----------------------------------------------------------------------------

    /// <summary>True between fights (before an encounter begins, or during a rest): the only times a mission is saved.</summary>
    public bool CanSave => !IsOver && ActiveSimulation is null;

    /// <summary>Snapshots the mission between fights. Throws during a fight or after the mission ended.</summary>
    public MissionSave CreateSave()
    {
        if (!CanSave)
            throw new InvalidOperationException("A mission can only be saved between fights.");

        return new MissionSave
        {
            MissionId = Mission.Id,
            BaseSeed = BaseSeed,
            StageIndex = StageIndex,
            Phase = Phase,
            RestBeatsRemaining = RestBeatsRemaining,
            RestOps = _restOps,
            RewardsEarned = _rewardsEarned.ToList(),
            RestLog = _restLog.Select(e => new RestLogEntry(e.ActorId, e.Message)).ToList(),
            Lance = Lance.Select(c => new CrestSave
            {
                CombatantId = c.Id,
                Name = c.Name,
                PilotId = c == Crest ? null : _pilotIds.GetValueOrDefault(c.Id),
                Assembly = c.Loadout.ExportConfiguration(),
                Components = c.Loadout.DamageOrder.Select(e => new ComponentSave(e.InstanceId, e.CurrentDurability, e.CurrentAmmo)).ToList(),
                Motes = c.Resources.CurrentMotes,
                FastIron = c.Resources.FastIron,
                CarriedFastIron = c.Loadout.CarriedFastIron,
                Consumables = c.Loadout.CarriedConsumables
            }).ToList()
        };
    }

    /// <summary>
    /// Rebuilds a saved mission. The player's Crest gets a fresh <see cref="Loadout"/> built from the save (the
    /// caller swaps it in for the assembly with the same id). Fails, with a reason, if the save no longer matches
    /// the content (an unknown mission, pilot, or component, or a stage past the mission's end).
    /// </summary>
    public static MissionRun? TryResume(MissionSave save, GameCatalog catalog, IReadOnlySet<string>? knownBlueprints, out string? error)
    {
        error = Validate(save, catalog);
        if (error is not null)
            return null;

        var mission = catalog.Missions[save.MissionId];
        var player = save.Lance[0];
        var allies = save.Lance.Skip(1)
            .Select(a => new Combatant(a.CombatantId, a.Name, Faction.Player, RestoreLoadout(a, catalog), new Vec2(0, 0), a.Motes)
            {
                Rules = catalog.Rules
            })
            .ToList();

        var run = new MissionRun(mission, RestoreLoadout(player, catalog), catalog, save.BaseSeed, knownBlueprints, allies)
        {
            StageIndex = save.StageIndex,
            Phase = save.Phase,
            RestBeatsRemaining = save.Phase == MissionPhase.Rest ? Math.Max(0, save.RestBeatsRemaining) : 0
        };
        foreach (var a in save.Lance.Skip(1))
            if (a.PilotId is not null)
                run._pilotIds[a.CombatantId] = a.PilotId;

        foreach (var (crest, saved) in run.Lance.Zip(save.Lance))
        {
            crest.Resources.TrySpendMotes(crest.Resources.CurrentMotes);
            crest.Resources.AddMotes(saved.Motes, int.MaxValue);
            crest.Resources.AddFastIron(saved.FastIron);
        }

        run._restOps = save.RestOps;
        run._rewardsEarned.AddRange(save.RewardsEarned);
        run._restLog.AddRange(save.RestLog.Select(e => new CombatEvent(-1, CombatEventType.FieldShaped, e.ActorId, e.Message)));
        return run;
    }

    private static string? Validate(MissionSave save, GameCatalog catalog)
    {
        if (save.Version > MissionSave.CurrentVersion)
            return $"The mission was saved by a newer version (save v{save.Version}).";
        if (!catalog.Missions.TryGetValue(save.MissionId, out var mission))
            return $"Mission '{save.MissionId}' no longer exists.";
        if (save.Phase is not (MissionPhase.Encounter or MissionPhase.Rest))
            return $"A {save.Phase} mission cannot be resumed.";
        if (save.StageIndex < 0 || save.StageIndex >= mission.EncounterIds.Count ||
            (save.Phase == MissionPhase.Rest && save.StageIndex >= mission.EncounterIds.Count - 1))
            return $"{mission.Name} has no stage {save.StageIndex + 1} to resume.";
        if (save.Lance.Count == 0 || save.Lance.Count > MaxAllies + 1)
            return "The saved lance is malformed.";
        if (save.Lance.Skip(1).Any(a => a.PilotId is not null && !catalog.Pilots.ContainsKey(a.PilotId)))
            return "A saved ally's pilot no longer exists.";
        if (save.Lance.Select(c => c.CombatantId).Distinct().Count() != save.Lance.Count ||
            save.Lance[0].CombatantId != EncounterFactory.PlayerCombatantId)
            return "The saved lance is malformed.";
        if (save.Lance.SelectMany(c => c.Assembly.ComponentIds).FirstOrDefault(id => !catalog.Equipment.ContainsKey(id)) is { } gone)
            return $"Component '{gone}' no longer exists.";
        if (save.Lance.Any(c => c.Components.Count != c.Assembly.ComponentIds.Count))
            return "A saved Crest's components don't match its assembly.";
        return null;
    }

    private static CrestLoadout RestoreLoadout(CrestSave saved, GameCatalog catalog)
    {
        var parts = saved.Assembly.ComponentIds.Select((id, i) =>
        {
            var state = saved.Components[i];
            var part = new EquipmentInstance(state.InstanceId, catalog.GetEquipment(id));
            part.RestoreState(state.Durability, state.Ammo);
            return part;
        });

        var loadout = new CrestLoadout(saved.Assembly.Id, saved.Assembly.Name, parts);
        foreach (var rule in saved.Assembly.ReactionRules.Where(r => catalog.Actions.TryGetValue(r.ActionId, out var a) && a.IsReactive))
            loadout.AddReactionRule(rule);
        loadout.SetCargoManifest(saved.Assembly.Cargo, catalog);
        loadout.RestoreCargo(saved.CarriedFastIron, saved.Consumables, catalog);
        return loadout;
    }

    /// <summary>The lance member with this id (null: the player's Crest).</summary>
    public Combatant? FindCrest(string? crestId) =>
        crestId is null ? Crest : Lance.FirstOrDefault(c => c.Id == crestId);

    public static bool IsUnlocked(MissionDefinition mission, WorkshopState workshop) =>
        mission.RequiredResearch.All(workshop.IsResearched) && mission.RequiredMissions.All(workshop.HasCompleted);

    // ---- Encounters ------------------------------------------------------------------------

    public CombatSimulation BeginEncounter()
    {
        if (Phase != MissionPhase.Encounter)
            throw new InvalidOperationException($"Cannot begin an encounter during {Phase}.");

        ActiveSimulation = EncounterFactory.CreateEncounter(CurrentEncounter, Crest, CurrentSeed, Catalog, Allies);
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

    /// <summary>Beats needed for the player's Crest to generate its next Mote (the rest's unit of time).</summary>
    public int MoteIntervalBeats => Crest.Stats.MoteGenerationIntervalBeats;

    /// <summary>
    /// Spends one Mote interval of rest time (the player's Crest's interval). Every operational Crest in the lance
    /// gains a Mote (wasted if its storage is full).
    /// </summary>
    public RestResult Wait()
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");

        int interval = MoteIntervalBeats;
        if (RestBeatsRemaining < interval)
            return RestResult.Fail($"Not enough rest time left (needs {interval} beats).");

        RestBeatsRemaining -= interval;
        var parts = new List<string>();
        foreach (var crest in Lance.Where(c => !c.IsDefeated))
        {
            var stats = crest.Stats;
            string who = crest == Crest ? "" : $"{crest.Name}: ";
            if (crest.Resources.CurrentMotes >= stats.MaxMotes)
                parts.Add($"{who}Mote storage already full ({stats.MaxMotes})");
            else
            {
                crest.Resources.AddMotes(1, stats.MaxMotes);
                parts.Add($"{who}+1 Mote ({crest.Resources.CurrentMotes}/{stats.MaxMotes})");
            }
        }

        return Log(RestResult.Ok($"Rested {interval} beats" + (parts.Count > 0 ? $": {string.Join("; ", parts)}." : ".")));
    }

    /// <summary>Casts Square Summon between Mote generations: spends its Mote cost for fast-iron.</summary>
    /// <param name="crestId">The lance member that casts, with its own Motes (null: the player's Crest).</param>
    public RestResult SquareSummon(string? crestId = null)
    {
        if (RestCrest(crestId, out var fail) is not { } crest)
            return fail;

        var summon = crest.Loadout.GetOperationalActionIds()
            .Select(Catalog.GetAction)
            .FirstOrDefault(a => a.FastIronProduced > 0);
        if (summon is null)
            return RestResult.Fail("No operational component can cast Square Summon.");
        if (summon.RequiresSpellbook && !crest.Stats.HasSpellbook)
            return RestResult.Fail($"{summon.Name} requires an operational Spellbook.");
        if (summon.MoteCost > crest.Stats.MaxMotes)
            return RestResult.Fail($"{summon.Name} needs {summon.MoteCost} Motes; this Crest can only store {crest.Stats.MaxMotes}.");
        if (!crest.Resources.TrySpendMotes(summon.MoteCost))
            return RestResult.Fail($"{summon.Name} needs {summon.MoteCost} Motes (have {crest.Resources.CurrentMotes}).");

        crest.Resources.AddFastIron(summon.FastIronProduced);
        return Log(RestResult.Ok($"{Who(crest)}{summon.Name}: {summon.MoteCost} Motes → {summon.FastIronProduced} fast-iron ({crest.TotalFastIron} on hand)."), crest);
    }

    /// <summary>
    /// Spends 1 fast-iron to restore up to <see cref="RulesConfig.RepairPointsPerFastIron"/> durability (a disabled
    /// part comes back online; a repaired Core brings a downed ally back into the lance).
    /// </summary>
    /// <param name="crestId">The lance member whose component is repaired, with its own fast-iron (null: the player's Crest).</param>
    public RestResult Repair(string componentInstanceId, string? crestId = null)
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");
        if (FindCrest(crestId) is not { } crest) // a downed Crest can still be repaired
            return RestResult.Fail("Unknown Crest.");

        var part = crest.Loadout.FindById(componentInstanceId);
        if (part is null)
            return RestResult.Fail("Unknown component.");
        if (part.CurrentDurability >= part.Definition.MaxDurability)
            return RestResult.Fail($"{part.Definition.Name} is undamaged.");

        if (!crest.TrySpendFastIron(1))
            return RestResult.Fail($"Repair needs 1 fast-iron (have {crest.TotalFastIron}).");

        part.RestoreDurability(Math.Max(1, crest.Rules.RepairPointsPerFastIron));
        return Log(RestResult.Ok($"{Who(crest)}Repaired {part.Definition.Name} ({part.CurrentDurability}/{part.Definition.MaxDurability}) for 1 fast-iron."), crest);
    }

    /// <summary>Shapes fast-iron into a component, consumable, or ammo (needs a Shaping spell on the Crest).</summary>
    /// <param name="crestId">The lance member that shapes, with its own fast-iron (null: the player's Crest).</param>
    public RestResult Shape(ShapingOrder order, string? ownComponentInstanceId = null, string? crestId = null)
    {
        if (RestCrest(crestId, out var fail) is not { } crest)
            return fail;

        var shaping = crest.Loadout.GetOperationalActionIds().Select(Catalog.GetAction).FirstOrDefault(a => a.IsShaping);
        if (shaping is null)
            return RestResult.Fail("No operational component can cast a Shaping spell.");
        if (shaping.RequiresSpellbook && !crest.Stats.HasSpellbook)
            return RestResult.Fail($"{shaping.Name} requires an operational Spellbook.");

        var command = new UseActionCommand(crest.Id, shaping.Id, OwnComponentInstanceId: ownComponentInstanceId, Shaping: order);
        var (check, plan) = ShapingRules.Validate(crest, shaping, command, Catalog);
        if (!check.IsValid || plan is null)
            return RestResult.Fail(check.FailureReason ?? "Cannot shape that.");

        crest.TrySpendFastIron(plan.FastIronCost);
        var events = new List<CombatEvent>();
        ShapingRules.Apply(crest, plan, $"{crest.Id}_rest{StageIndex}_{++_restOps}", -1, events);
        _restLog.AddRange(events);
        return RestResult.Ok(events.LastOrDefault()?.Message ?? "Shaped.");
    }

    /// <summary>Uses a cargo item that works on the Crest itself (e.g. an Ammo Pack into a weapon).</summary>
    /// <param name="crestId">The lance member that uses its own cargo (null: the player's Crest).</param>
    public RestResult UseCargoItem(string actionId, string ownComponentInstanceId, string? crestId = null)
    {
        if (RestCrest(crestId, out var fail) is not { } crest)
            return fail;
        if (!Catalog.Actions.TryGetValue(actionId, out var action) || !action.RefillsAmmo || action.ConsumableItemId is null)
            return RestResult.Fail("That item cannot be used during a rest.");
        if (crest.Loadout.FindOperationalProviderForAction(action.Id) is null)
            return RestResult.Fail("No operational cargo component holds it.");

        var command = new UseActionCommand(crest.Id, action.Id, OwnComponentInstanceId: ownComponentInstanceId);
        var (check, plan) = ShapingRules.Validate(crest, action, command, Catalog);
        if (!check.IsValid || plan?.AmmoTarget is not { } weapon)
            return RestResult.Fail(check.FailureReason ?? "Cannot use that.");
        if (!crest.Loadout.TryTakeConsumable(action.ConsumableItemId))
            return RestResult.Fail("None left in cargo.");

        weapon.RefillAmmo();
        return Log(RestResult.Ok($"{Who(crest)}{action.Name}: {weapon.Definition.Name} restocked ({weapon.CurrentAmmo}/{weapon.Definition.MaxAmmo})."), crest);
    }

    /// <summary>
    /// Hands fast-iron from one lance member to another (loose fast-iron first, then cargo). Free, and takes no time.
    /// A downed Crest can still receive fast-iron, so it can be repaired.
    /// </summary>
    /// <param name="fromCrestId">The giver (null: the player's Crest).</param>
    /// <param name="toCrestId">The receiver (null: the player's Crest).</param>
    public RestResult GiveFastIron(string? fromCrestId, string? toCrestId, int count = 1)
    {
        if (Phase != MissionPhase.Rest)
            return RestResult.Fail("Not resting.");
        if (FindCrest(fromCrestId) is not { } from || FindCrest(toCrestId) is not { } to)
            return RestResult.Fail("Unknown Crest.");
        if (from == to)
            return RestResult.Fail("Pick another Crest to give fast-iron to.");
        if (count < 1)
            return RestResult.Fail("Give at least 1 fast-iron.");
        if (!from.TrySpendFastIron(count))
            return RestResult.Fail($"{from.Name} has only {from.TotalFastIron} fast-iron.");

        to.Resources.AddFastIron(count);
        return Log(RestResult.Ok($"{from.Name} gave {count} fast-iron to {to.Name} ({to.TotalFastIron} on hand)."), to);
    }

    /// <summary>An operational lance member, resting; otherwise null with the reason.</summary>
    private Combatant? RestCrest(string? crestId, out RestResult fail)
    {
        fail = RestResult.Fail("Not resting.");
        if (Phase != MissionPhase.Rest)
            return null;

        var crest = FindCrest(crestId);
        if (crest is null)
            fail = RestResult.Fail("Unknown Crest.");
        else if (crest.IsDefeated)
            fail = RestResult.Fail($"{crest.Name} is down: repair its Core first.");
        return crest is { IsDefeated: false } ? crest : null;
    }

    private string Who(Combatant crest) => crest == Crest ? "" : $"{crest.Name}: ";

    /// <summary>Ends the rest (any remaining time is lost) and readies the next encounter.</summary>
    public void ContinueToNextEncounter()
    {
        if (Phase != MissionPhase.Rest)
            throw new InvalidOperationException("Not resting.");

        StageIndex++;
        RestBeatsRemaining = 0;
        Phase = MissionPhase.Encounter;
    }

    private RestResult Log(RestResult result, Combatant? crest = null)
    {
        _restLog.Add(new CombatEvent(-1, CombatEventType.FieldShaped, (crest ?? Crest).Id, result.Message));
        return result;
    }
}
