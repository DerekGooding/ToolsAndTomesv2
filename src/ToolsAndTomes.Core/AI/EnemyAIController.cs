using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.AI;

/// <summary>
/// Drives every enemy Crest. Each enemy picks a target among the player's lance (the player and allies): the
/// nearest one, but it keeps its current target unless another is clearly closer, so it doesn't flip-flop.
/// Ordinary enemies issue one command when idle; a <see cref="EnemyArchetype.Warlord"/> fights like
/// <see cref="AutoPilot"/>, committing every affordable attack each beat.
/// </summary>
public sealed class EnemyAIController
{
    /// <summary>How much closer (tiles) another Crest must be before an enemy switches targets.</summary>
    public const double TargetSwitchMargin = 3.0;

    private readonly int _decisionIntervalBeats;
    private readonly Dictionary<string, string> _targets = new(StringComparer.Ordinal);
    private readonly AutoPilot _warlordPilot = new();

    public EnemyAIController(int decisionIntervalBeats = 1)
    {
        _decisionIntervalBeats = Math.Max(1, decisionIntervalBeats);
    }

    public void Update(CombatSimulation simulation)
    {
        if (simulation.IsPaused || simulation.Outcome != EncounterOutcome.InProgress)
            return;

        if (simulation.CurrentBeat % _decisionIntervalBeats != 0)
            return;

        var lance = simulation.Combatants
            .Where(c => c.Faction == Faction.Player && !c.IsDefeated)
            .ToList();

        if (lance.Count == 0)
            return;

        foreach (var enemy in simulation.Combatants)
        {
            if (enemy.Faction != Faction.Enemy || enemy.IsDefeated)
                continue;

            if (enemy.Archetype == EnemyArchetype.Warlord)
            {
                _warlordPilot.Update(simulation, enemy, ChooseTarget(enemy, lance));
                continue;
            }

            if (enemy.State != ActionState.Idle || enemy.HasQueuedCommands)
                continue;

            var target = ChooseTarget(enemy, lance);
            var command = ChooseCommand(enemy, target, simulation);
            if (command is not null)
            {
                simulation.IssueCommand(command);
            }
        }
    }

    /// <summary>The nearest living Crest of the lance, unless the current target is still within <see cref="TargetSwitchMargin"/> of it.</summary>
    public Combatant ChooseTarget(Combatant enemy, IReadOnlyList<Combatant> lance)
    {
        var nearest = lance
            .OrderBy(c => c.Position.DistanceTo(enemy.Position))
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .First();

        if (_targets.TryGetValue(enemy.Id, out var currentId) &&
            lance.FirstOrDefault(c => c.Id == currentId) is { } current &&
            current.Position.DistanceTo(enemy.Position) <= nearest.Position.DistanceTo(enemy.Position) + TargetSwitchMargin)
            return current;

        _targets[enemy.Id] = nearest.Id;
        return nearest;
    }

    public CombatCommand? ChooseCommand(Combatant enemy, Combatant target, CombatSimulation sim)
    {
        double dist = enemy.Position.DistanceTo(target.Position);

        return enemy.Archetype switch
        {
            EnemyArchetype.Skirmisher => DecideSkirmisher(enemy, target, dist, sim),
            EnemyArchetype.Bruiser => DecideBruiser(enemy, target, sim),
            EnemyArchetype.Artillery => DecideArtillery(enemy, target, dist, sim),
            _ => TryBestOffensiveAction(enemy, target, sim)
        };
    }

    private CombatCommand? DecideSkirmisher(Combatant enemy, Combatant target, double dist, CombatSimulation sim)
    {
        if (dist < 4.0)
        {
            var away = (enemy.Position - target.Position).Normalized();
            var retreat = (enemy.Position + away * 4.0).Clamp(1.0, sim.ArenaWidth - 1.0, 1.0, sim.ArenaHeight - 1.0);
            if (retreat.DistanceTo(enemy.Position) > 0.8)
                return new MoveCommand(enemy.Id, retreat);
        }

        return TryAction(enemy, "weapon_talon", target, null, sim)
            ?? TryBestOffensiveAction(enemy, target, sim);
    }

    private CombatCommand? DecideBruiser(Combatant enemy, Combatant target, CombatSimulation sim)
    {
        return TryAction(enemy, "weapon_titan", target, null, sim)
            ?? TryAction(enemy, "weapon_righteous", target, null, sim)
            ?? TryBestOffensiveAction(enemy, target, sim);
    }

    private CombatCommand? DecideArtillery(Combatant enemy, Combatant target, double dist, CombatSimulation sim)
    {
        if (dist < 5.0)
        {
            var away = (enemy.Position - target.Position).Normalized();
            var retreat = (enemy.Position + away * 4.5).Clamp(1.0, sim.ArenaWidth - 1.0, 1.0, sim.ArenaHeight - 1.0);
            if (retreat.DistanceTo(enemy.Position) > 0.8)
                return new MoveCommand(enemy.Id, retreat);
        }

        return TryAction(enemy, "weapon_artillery_mortar", null, target.Position, sim)
            ?? TryBestOffensiveAction(enemy, target, sim);
    }

    private CombatCommand? TryAction(
        Combatant enemy,
        string actionId,
        Combatant? target,
        Vec2? targetPoint,
        CombatSimulation sim)
    {
        if (!sim.Catalog.Actions.TryGetValue(actionId, out var def))
            return null;

        var validation = ActionValidationRules.ValidateUseAction(enemy, def, target, targetPoint);
        if (!validation.IsValid)
            return null;

        return new UseActionCommand(enemy.Id, actionId, target?.Id, targetPoint);
    }

    private CombatCommand? TryBestOffensiveAction(Combatant enemy, Combatant target, CombatSimulation sim)
    {
        foreach (var actionId in enemy.Loadout.GetOperationalActionIds())
        {
            if (!sim.Catalog.Actions.TryGetValue(actionId, out var def))
                continue;

            if (def.Category != ActionCategory.Weapon && def.Category != ActionCategory.Spell)
                continue;

            Vec2? point = def.Targeting == TargetingType.Point ? target.Position : null;
            Combatant? unitTarget = def.Targeting == TargetingType.EnemyUnit ? target : null;

            if (ActionValidationRules.ValidateUseAction(enemy, def, unitTarget, point).IsValid)
                return new UseActionCommand(enemy.Id, actionId, unitTarget?.Id, point);
        }

        return null;
    }
}
