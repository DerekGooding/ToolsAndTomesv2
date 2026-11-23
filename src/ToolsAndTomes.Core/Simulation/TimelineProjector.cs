using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public static class TimelineProjector
{
    /// <summary>One entry per running action, movement order, and locked-out component.</summary>
    public static IReadOnlyList<TimelineEntry> BuildEntries(IEnumerable<Combatant> combatants)
    {
        var entries = new List<TimelineEntry>();
        foreach (var actor in combatants)
        {
            if (actor.IsDefeated)
                continue;

            foreach (var act in actor.ActionsInMotion)
            {
                if (act.Phase == ActionState.Moving)
                {
                    entries.Add(new TimelineEntry(
                        actor.Id, actor.Name, actor.Faction, ActionState.Moving,
                        $"Approaching → {act.Definition.Name}", act.Definition.Telegraph, act.TargetActorId,
                        act.TargetPoint, act.Definition.AreaRadius, 1, 1, -1, false));
                    continue;
                }

                entries.Add(new TimelineEntry(
                    actor.Id,
                    actor.Name,
                    actor.Faction,
                    act.Phase,
                    act.Definition.Name,
                    act.Definition.Telegraph,
                    act.TargetActorId,
                    act.TargetPoint,
                    act.Definition.AreaRadius,
                    act.PhaseBeatsRemaining,
                    Math.Max(1, act.PhaseTotalBeats),
                    act.BeatsUntilImpact(),
                    act.IsInterruptiblePhase && !act.Definition.IsUninterruptible));
            }

            if (actor.Movement is { ForActionId: null } move && move.Destination is { } dest)
            {
                double dist = actor.Position.DistanceTo(dest);
                int estBeats = Math.Max(1, (int)Math.Ceiling(dist / Math.Max(1, actor.Stats.MoveSpeed)));
                entries.Add(new TimelineEntry(
                    actor.Id, actor.Name, actor.Faction, ActionState.Moving,
                    "Moving", null, null, dest, 0, estBeats, estBeats, -1, false));
            }

            foreach (var (componentId, (remaining, total)) in actor.ComponentLockouts)
            {
                string name = actor.Loadout.FindById(componentId)?.Definition.Name ?? componentId;
                entries.Add(new TimelineEntry(
                    actor.Id, actor.Name, actor.Faction, ActionState.Interrupted,
                    $"Interrupted: {name}", null, null, null, 0, remaining, Math.Max(1, total), -1, false));
            }
        }

        return entries;
    }
}
