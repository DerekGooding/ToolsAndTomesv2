using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.AI;

/// <summary>
/// A simple, build-agnostic controller for any Crest: every beat, it commits every offensive action it can
/// afford against the nearest enemy (one per component, within available Wit). Defensive play comes from the
/// Crest's own reaction rules. Used for headless balance runs; deliberately unclever so that results reflect
/// the build rather than the pilot.
/// </summary>
public sealed class AutoPilot
{
    public void Update(CombatSimulation sim, Combatant crest)
    {
        if (sim.IsPaused || sim.Outcome != EncounterOutcome.InProgress || crest.IsDefeated)
            return;

        var target = sim.Combatants
            .Where(c => c.Faction != crest.Faction && !c.IsDefeated)
            .OrderBy(c => c.Position.DistanceTo(crest.Position))
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (target is null)
            return;

        int witBudget = crest.Stats.AvailableWit(crest.Resources.ReservedWit);
        var busyActions = crest.ActionsInMotion.Select(a => a.Definition.Id)
            .Concat(crest.QueuedCommands.OfType<UseActionCommand>().Select(c => c.ActionId))
            .ToHashSet();

        // Strongest actions first, so limited Wit goes to the biggest hits.
        var candidates = crest.Loadout.GetOperationalActionIds()
            .Select(id => sim.Catalog.GetAction(id))
            .Where(a => a.Category is ActionCategory.Weapon or ActionCategory.Spell)
            .Where(a => !a.IsReactive && a.Targeting != TargetingType.Self)
            .Where(a => !busyActions.Contains(a.Id))
            .OrderByDescending(a => a.BaseDamage * a.HitCount)
            .ThenBy(a => a.Id, StringComparer.Ordinal);

        foreach (var action in candidates)
        {
            if (action.WitReserve > witBudget || crest.FindFreeProvider(action.Id) is null)
                continue;

            Combatant? unit = action.Targeting == TargetingType.EnemyUnit ? target : null;
            Vec2? point = action.Targeting == TargetingType.Point ? target.Position : null;

            if (!sim.ValidateUse(crest, action, unit, point).IsValid)
                continue;

            if (sim.IssueCommand(new UseActionCommand(crest.Id, action.Id, unit?.Id, point)).IsValid)
                witBudget -= action.WitReserve;
        }
    }
}
