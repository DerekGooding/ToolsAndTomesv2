using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public static class TimelineProjector
{
    public static IReadOnlyList<TimelineEntry> BuildEntries(IEnumerable<Combatant> combatants)
    {
        var entries = new List<TimelineEntry>();
        foreach (var actor in combatants)
        {
            if (actor.IsDefeated)
                continue;

            if (actor.State == ActionState.Interrupted)
            {
                entries.Add(new TimelineEntry(
                    actor.Id, actor.Name, actor.Faction, ActionState.Interrupted,
                    "Interrupted", null, null, null, 0,
                    actor.InterruptedBeatsRemaining, Math.Max(1, actor.InterruptedTotalBeats), -1, false));
            }
            else if (actor.CurrentAction is { } act && actor.State != ActionState.Moving)
            {
                entries.Add(new TimelineEntry(
                    actor.Id,
                    actor.Name,
                    actor.Faction,
                    actor.State,
                    act.Definition.Name,
                    act.Definition.Telegraph,
                    act.TargetActorId,
                    act.TargetPoint,
                    act.Definition.AreaRadius,
                    act.PhaseBeatsRemaining,
                    Math.Max(1, act.PhaseTotalBeats),
                    act.BeatsUntilImpact(),
                    actor.State is ActionState.Windup or ActionState.Executing));
            }
            else if (actor.State == ActionState.Moving)
            {
                Vec2? dest = actor.MoveDestination;
                string label = actor.CurrentAction is { } pending ? $"Moving → {pending.Definition.Name}" : "Moving";
                double dist = dest is { } d ? actor.Position.DistanceTo(d) : 0;
                int estBeats = Math.Max(1, (int)Math.Ceiling(dist / Math.Max(1, actor.Stats.MoveSpeed)));
                entries.Add(new TimelineEntry(
                    actor.Id, actor.Name, actor.Faction, ActionState.Moving,
                    label, null, null, dest, 0, estBeats, estBeats, -1, false));
            }
        }

        return entries;
    }
}
