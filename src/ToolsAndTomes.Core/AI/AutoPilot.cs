using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.AI;

/// <summary>
/// A simple, build-agnostic controller for any Crest: every beat, it commits every offensive action it can
/// afford against the nearest enemy (one per component, within available Wit), and keeps walking toward a target
/// that stays beyond its damaging reach. Defensive play comes from the
/// Crest's own reaction rules. Used for headless balance runs; deliberately unclever so that results reflect
/// the build rather than the pilot.
/// </summary>
public sealed class AutoPilot
{
    /// <summary>
    /// Tome support spells, cast before attacks while Wit allows: Ward Ally on the most-threatened lance member
    /// without a ward; Bulwark when two or more of the lance are close and under threat; Quick Mend on a lance
    /// member with a broken part; Conjured Blade into a free hand. Returns the Wit committed.
    /// </summary>
    private static int CastSupport(CombatSimulation sim, Combatant crest, HashSet<string> busy, int witBudget)
    {
        int spent = 0;
        var lance = sim.Combatants.Where(c => c.Faction == crest.Faction && !c.IsDefeated).ToList();
        bool Threatened(Combatant c) => sim.GetIncomingImpacts(c).Count > 0;

        foreach (var action in crest.Loadout.GetOperationalActionIds().Select(sim.Catalog.GetAction)
                     .Where(a => !a.IsReactive && !busy.Contains(a.Id))
                     .OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            if (action.WitReserve > witBudget - spent || crest.FindFreeProvider(action.Id) is null)
                continue;

            Combatant? ally = null;
            bool cast = false;
            if (action.Targeting == TargetingType.AllyUnit && action.AegisBlocks > 0)
            {
                ally = lance.Where(c => Threatened(c) && !c.HasActiveAegis && c.Position.DistanceTo(crest.Position) <= action.Range)
                    .OrderByDescending(c => c.Id == EncounterFactory.PlayerCombatantId)
                    .ThenBy(c => c.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                cast = ally is not null;
            }
            else if (action.Targeting == TargetingType.AllyUnit && action.MendPoints > 0)
            {
                ally = lance.Where(c => c.Position.DistanceTo(crest.Position) <= action.Range &&
                                        c.Loadout.Installed.Any(e => e.IsDisabled || (e.Definition.IsCore && e.CurrentDurability < e.Definition.MaxDurability)))
                    .OrderByDescending(c => c.Loadout.Installed.Count(e => e.IsDisabled))
                    .ThenBy(c => c.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                cast = ally is not null;
            }
            else if (action.Targeting == TargetingType.Self && action.ProtectionBonusDice > 0)
            {
                var near = lance.Where(c => c.Position.DistanceTo(crest.Position) <= action.AreaRadius).ToList();
                cast = near.Count >= 2 && near.Any(Threatened);
            }
            else if (action.Targeting == TargetingType.Self && action.ConjuresComponentId is not null)
            {
                cast = crest.TotalFastIron > action.FastIronSpent; // keep one fast-iron in hand for repairs
            }

            if (cast && sim.ValidateUse(crest, action, ally, null).IsValid &&
                sim.IssueCommand(new UseActionCommand(crest.Id, action.Id, ally?.Id)).IsValid)
            {
                spent += action.WitReserve;
                busy.Add(action.Id);
            }
        }

        return spent;
    }

    /// <param name="target">The enemy to fight; the nearest living enemy when omitted.</param>
    public void Update(CombatSimulation sim, Combatant crest, Combatant? target = null)
    {
        if (sim.IsPaused || sim.Outcome != EncounterOutcome.InProgress || crest.IsDefeated)
            return;

        if (target is null || target.IsDefeated || target.Faction == crest.Faction)
            target = sim.Combatants
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

        witBudget -= CastSupport(sim, crest, busyActions, witBudget);

        // Strongest actions first, so limited Wit goes to the biggest hits.
        var candidates = crest.Loadout.GetOperationalActionIds()
            .Select(id => sim.Catalog.GetAction(id))
            .Where(a => a.Category is ActionCategory.Weapon or ActionCategory.Spell)
            .Where(a => !a.IsReactive && a.Targeting is TargetingType.EnemyUnit or TargetingType.Point)
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

        // Keep closing on a target that stays out of reach when nothing else is driving the legs.
        int reach = crest.Loadout.GetOperationalActionIds()
            .Select(id => sim.Catalog.GetAction(id))
            .Where(a => a.BaseDamage > 0 && !a.IsReactive)
            .Select(a => a.Range)
            .DefaultIfEmpty(0)
            .Max();
        bool legsBusy = crest.Movement is not null ||
                        crest.QueuedCommands.Any(c => c is MoveCommand or UseActionCommand) ||
                        crest.ActionsInMotion.Any(a => a.Phase == ActionState.Moving);
        if (!legsBusy && reach > 0 && crest.Position.DistanceTo(target.Position) > reach && crest.FindLocomotionProvider() is not null)
            sim.IssueCommand(new MoveCommand(crest.Id, target.Position));
    }
}
