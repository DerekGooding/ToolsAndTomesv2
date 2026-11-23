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

public sealed class CombatSimulation
{
    private readonly List<Combatant> _combatants = [];
    private readonly List<CombatEvent> _events = [];
    private readonly GameCatalog _catalog;
    private readonly DamageResolutionService _damageService;
    private readonly ActorStateProcessor _stateProcessor;
    private readonly ISeededRandom _rng;

    public int CurrentTick { get; private set; }
    public double ElapsedSeconds => CurrentTick / (double)ActionCatalog.TicksPerSecond;
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
        _damageService = damageService ?? new DamageResolutionService();
        ArenaWidth = arenaWidth;
        ArenaHeight = arenaHeight;
        _stateProcessor = new ActorStateProcessor(_catalog, ArenaWidth, ArenaHeight);
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void TogglePause() => IsPaused = !IsPaused;

    public Combatant? FindCombatant(string id) => _combatants.FirstOrDefault(c => c.Id == id);

    public ActionValidationResult IssueCommand(CombatCommand command)
    {
        var actor = FindCombatant(command.ActorId);
        if (actor is null || actor.IsDefeated)
            return ActionValidationResult.Fail("Actor not found or defeated.");

        if (command is CancelActionCommand)
        {
            _stateProcessor.CancelCurrentAction(actor);
            return ActionValidationResult.Valid;
        }

        if (command is UseActionCommand useCmd)
        {
            if (!_catalog.Actions.TryGetValue(useCmd.ActionId, out var def))
                return ActionValidationResult.Fail("Unknown action.");

            var target = useCmd.TargetActorId is not null ? FindCombatant(useCmd.TargetActorId) : null;
            var validation = ActionValidationRules.ValidateUseAction(actor, def, target, useCmd.TargetPoint);
            if (!validation.IsValid)
                return validation;

            if (def.IsReactive && actor.State is ActionState.Idle or ActionState.Moving or ActionState.Windup)
            {
                if (actor.State == ActionState.Windup)
                    _stateProcessor.CancelCurrentAction(actor);

                _stateProcessor.StartUseAction(actor, useCmd, def, _combatants, CurrentTick, _events);
                return ActionValidationResult.Valid;
            }
        }

        if (actor.State is ActionState.Idle or ActionState.Moving)
        {
            _stateProcessor.ApplyCommandImmediately(actor, command, _combatants, CurrentTick, _events);
        }
        else if (actor.State == ActionState.Windup && command is MoveCommand)
        {
            _stateProcessor.CancelCurrentAction(actor);
            _stateProcessor.ApplyCommandImmediately(actor, command, _combatants, CurrentTick, _events);
        }
        else
        {
            actor.QueuedCommand = command;
        }

        return ActionValidationResult.Valid;
    }

    public void StepTick()
    {
        if (IsPaused || Outcome != EncounterOutcome.InProgress)
            return;

        CurrentTick++;
        var incomingHits = new List<IncomingHit>();

        foreach (var actor in _combatants)
        {
            if (actor.IsDefeated)
                continue;

            _stateProcessor.UpdatePassiveTimers(actor, CurrentTick, _events);
            _stateProcessor.AdvanceActorState(actor, _combatants, CurrentTick, incomingHits, _events);
        }

        var batchesByActor = incomingHits
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
                CurrentTick,
                _rng,
                ArenaWidth,
                ArenaHeight,
                _events);
        }

        UpdateOutcome();
    }

    public IReadOnlyList<TimelineEntry> GetTimelineEntries() =>
        TimelineProjector.BuildEntries(_combatants);

    private void UpdateOutcome()
    {
        bool anyPlayerAlive = _combatants.Any(c => c.Faction == Faction.Player && !c.IsDefeated);
        bool anyEnemyAlive = _combatants.Any(c => c.Faction == Faction.Enemy && !c.IsDefeated);

        if (!anyPlayerAlive)
            Outcome = EncounterOutcome.PlayerDefeat;
        else if (!anyEnemyAlive)
            Outcome = EncounterOutcome.PlayerVictory;
    }
}
