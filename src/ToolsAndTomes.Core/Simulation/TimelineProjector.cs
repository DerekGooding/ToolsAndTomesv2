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

            if (actor.CurrentAction is { } act)
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
                    act.PhaseTicksRemaining,
                    Math.Max(1, act.PhaseTotalTicks),
                    act.TicksUntilExecution(),
                    actor.State is ActionState.Windup or ActionState.Executing));
            }
            else if (actor.State == ActionState.Moving && actor.MoveDestination is { } dest)
            {
                double dist = actor.Position.DistanceTo(dest);
                int estTicks = (int)Math.Ceiling(dist / Math.Max(0.5, actor.Stats.MoveSpeed) * ActionCatalog.TicksPerSecond);
                entries.Add(new TimelineEntry(
                    actor.Id, actor.Name, actor.Faction, ActionState.Moving,
                    "Moving", null, null, dest, 0, estTicks, Math.Max(1, estTicks), -1, false));
            }
            else if (actor.State == ActionState.Interrupted)
            {
                entries.Add(new TimelineEntry(
                    actor.Id, actor.Name, actor.Faction, ActionState.Interrupted,
                    "Interrupted", null, null, null, 0, actor.InterruptedTicksRemaining, 8, -1, false));
            }
        }

        return entries;
    }
}
