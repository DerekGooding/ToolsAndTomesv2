using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

public enum EncounterOutcome
{
    InProgress,
    PlayerVictory,
    PlayerDefeat
}

/// <summary>
/// Deterministic beat-based combat simulation. One beat equals one tabletop second.
/// Every rule resolves on beat boundaries in a fixed order that can be executed by hand at a table:
/// <list type="number">
/// <item>Passive timers (Motes, heat, Aegis, evasion, interruption stun).</item>
/// <item>Reaction rules are checked for Crests that declared no reaction; reactions resolve, then their hits resolve.</item>
/// <item>Actions in motion consume the beat; impact windows that end this beat resolve.</item>
/// <item>Impact hits resolve per target (sorted by combatant id); a Crest whose Wit budget went negative drops its newest commitments.</item>
/// <item>Movement.</item>
/// <item>Buffered commands take effect for the next beat.</item>
/// <item>Cooldowns count down.</item>
/// </list>
/// Real-time presentation may interpolate between beats, but never changes these results.
/// </summary>
public sealed class CombatSimulation
{
    private readonly List<Combatant> _combatants = [];
    private readonly List<CombatEvent> _events = [];
    private readonly GameCatalog _catalog;
    private readonly DamageResolutionService _damageService;
    private readonly ActorStateProcessor _stateProcessor;
    private readonly ISeededRandom _rng;

    /// <summary>The beat currently in progress (0-based). Also equals elapsed tabletop seconds.</summary>
    public int CurrentBeat { get; private set; }
    public bool IsPaused { get; private set; }
    public double ArenaWidth { get; }
    public double ArenaHeight { get; }
    public EncounterOutcome Outcome { get; private set; } = EncounterOutcome.InProgress;

    public IReadOnlyList<Combatant> Combatants => _combatants;
    public IReadOnlyList<CombatEvent> Events => _events;
    public GameCatalog Catalog => _catalog;

    public CombatSimulation(
        IEnumerable<Combatant> combatants,
        int seed = 1337,
        GameCatalog? catalog = null,
        DamageResolutionService? damageService = null,
        double arenaWidth = 24.0,
        double arenaHeight = 14.0)
        : this(combatants, new SeededRandom(seed), catalog, damageService, arenaWidth, arenaHeight)
    {
    }

    public CombatSimulation(
        IEnumerable<Combatant> combatants,
        ISeededRandom rng,
        GameCatalog? catalog = null,
        DamageResolutionService? damageService = null,
        double arenaWidth = 24.0,
        double arenaHeight = 14.0)
    {
        _combatants.AddRange(combatants.OrderBy(c => c.Id, StringComparer.Ordinal));
        _rng = rng;
        _catalog = catalog ?? GameCatalog.Default;
        _damageService = damageService ?? new DamageResolutionService(_catalog.Rules);
        foreach (var c in _combatants)
            c.Rules = _catalog.Rules;
        ArenaWidth = arenaWidth;
        ArenaHeight = arenaHeight;
        _stateProcessor = new ActorStateProcessor(_catalog, ArenaWidth, ArenaHeight);
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void TogglePause() => IsPaused = !IsPaused;

    public Combatant? FindCombatant(string id) => _combatants.FirstOrDefault(c => c.Id == id);

    /// <summary>Validates an action with full battlefield context (e.g. reaction windows), without issuing it.</summary>
    public ActionValidationResult ValidateUse(Combatant actor, ActionDefinition action, Combatant? target, Vec2? targetPoint) =>
        ActionValidationRules.ValidateUseAction(actor, action, target, targetPoint, _combatants);

    /// <summary>Enemy impacts currently landing on <paramref name="defender"/> (the reaction window).</summary>
    public IReadOnlyList<IncomingImpact> GetIncomingImpacts(Combatant defender) =>
        ImpactRules.GetIncomingImpacts(defender, _combatants);

    /// <summary>
    /// Validates and buffers a command. Nothing changes until the next beat boundary:
    /// reactions resolve at the end of the current beat; all other commands take effect at the start of the next beat.
    /// </summary>
    public ActionValidationResult IssueCommand(CombatCommand command)
    {
        var actor = FindCombatant(command.ActorId);
        if (actor is null || actor.IsDefeated)
            return ActionValidationResult.Fail("Actor not found or defeated.");

        switch (command)
        {
            case CancelActionCommand:
                actor.PendingCancel = true;
                actor.ClearQueuedCommands();
                return ActionValidationResult.Valid;

            case UseActionCommand use:
            {
                if (!_catalog.Actions.TryGetValue(use.ActionId, out var def))
                    return ActionValidationResult.Fail("Unknown action.");

                var target = use.TargetActorId is not null ? FindCombatant(use.TargetActorId) : null;
                var validation = ActionValidationRules.ValidateUseAction(actor, def, target, use.TargetPoint, _combatants);
                if (!validation.IsValid)
                    return validation;

                var (shapingCheck, _) = ShapingRules.Validate(actor, def, use, _catalog);
                if (!shapingCheck.IsValid)
                    return shapingCheck;

                if (def.IsReactive)
                    actor.AddPendingReaction(use);
                else
                    actor.QueueCommand(use);

                return ActionValidationResult.Valid;
            }

            default:
                actor.QueueCommand(command);
                return ActionValidationResult.Valid;
        }
    }

    /// <summary>Resolves the end of the current beat and advances to the next one.</summary>
    public void StepBeat()
    {
        if (IsPaused || Outcome != EncounterOutcome.InProgress)
            return;

        int beat = CurrentBeat;

        // 1. Passive timers
        foreach (var actor in Living())
            _stateProcessor.UpdatePassiveTimers(actor, beat, _events);

        // 2. Reactions: Crests without a declared reaction check their reaction rules; then all reactions
        //    resolve before any impact
        foreach (var actor in Living())
            _stateProcessor.EvaluateReactionRules(actor, _combatants, beat, _events);

        var reactionHits = new List<IncomingHit>();
        foreach (var actor in Living())
            _stateProcessor.ResolvePendingReactions(actor, _combatants, beat, reactionHits, _events);
        ResolveHits(reactionHits, beat);

        // 3. Actions in motion consume this beat; finished impact windows produce hits
        var impactHits = new List<IncomingHit>();
        foreach (var actor in Living())
            _stateProcessor.AdvanceActions(actor, _combatants, beat, impactHits, _events);

        // 4. Impacts resolve simultaneously, batched per target
        ResolveHits(impactHits, beat);

        // 5. Movement
        foreach (var actor in Living())
            _stateProcessor.AdvanceMovement(actor, _combatants, beat, _events);

        // 6. Buffered commands take effect for the next beat
        foreach (var actor in Living())
            _stateProcessor.ApplyBufferedCommands(actor, _combatants, beat, _events);

        // 7. Cooldowns
        foreach (var actor in Living())
            actor.TickCooldowns();

        UpdateOutcome();
        CurrentBeat++;
    }

    /// <summary>
    /// Where the combatant's movement will place it at the end of the current beat, absent pushes or blinks.
    /// Presentation layers interpolate toward this.
    /// </summary>
    public Vec2 ProjectEndOfBeatPosition(Combatant actor) =>
        actor.Movement is not null
            ? _stateProcessor.ComputeMovement(actor, _combatants).Next
            : actor.Position;

    public IReadOnlyList<TimelineEntry> GetTimelineEntries() =>
        TimelineProjector.BuildEntries(_combatants);

    private IEnumerable<Combatant> Living() => _combatants.Where(c => !c.IsDefeated).ToList();

    private void ResolveHits(List<IncomingHit> hits, int beat)
    {
        if (hits.Count == 0)
            return;

        var batchesByActor = hits
            .GroupBy(h => h.TargetActorId)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in batchesByActor)
        {
            var target = FindCombatant(group.Key);
            if (target is null)
                continue;

            _damageService.ResolveActorBatch(
                target,
                group.ToList(),
                beat,
                _rng,
                ArenaWidth,
                ArenaHeight,
                _events);

            if (target.IsDefeated)
                continue;

            var dropped = target.EnforceWitBudget();
            if (dropped.Count > 0)
            {
                _events.Add(new CombatEvent(beat, CombatEventType.WitCollapse, target.Id,
                    $"{target.Name}'s Wit collapses! Dropped: {string.Join(", ", dropped)}."));
            }
        }
    }

    /// <summary>
    /// The fight is lost when the player's own Crest falls (allies fighting on don't save it), or, in a fight
    /// without one, when every Player-faction Crest is down. It is won when every enemy is down.
    /// </summary>
    private void UpdateOutcome()
    {
        var lead = FindCombatant(EncounterFactory.PlayerCombatantId);
        bool playerDown = lead?.IsDefeated ?? !_combatants.Any(c => c.Faction == Faction.Player && !c.IsDefeated);
        bool anyEnemyAlive = _combatants.Any(c => c.Faction == Faction.Enemy && !c.IsDefeated);

        if (playerDown)
            Outcome = EncounterOutcome.PlayerDefeat;
        else if (!anyEnemyAlive)
            Outcome = EncounterOutcome.PlayerVictory;
    }
}
