using System.Text;

namespace ToolsAndTomes.Core.Simulation;

/// <summary>
/// Formats a combat log beat by beat, in resolution order, so a fight can be replayed or checked by hand at a table.
/// </summary>
public static class TabletopTranscript
{
    public static IReadOnlyList<string> Lines(IEnumerable<CombatEvent> events, bool includeMoteTicks = false) =>
        events
            .Where(e => includeMoteTicks || e.Type != CombatEventType.MoteGenerated)
            .Select(e => $"Beat {e.Beat}: {e.Message}")
            .ToList();

    public static string Format(IEnumerable<CombatEvent> events, bool includeMoteTicks = false)
    {
        var sb = new StringBuilder();
        foreach (var group in events
                     .Where(e => includeMoteTicks || e.Type != CombatEventType.MoteGenerated)
                     .GroupBy(e => e.Beat))
        {
            sb.AppendLine($"— Beat {group.Key} —");
            foreach (var e in group)
                sb.AppendLine($"  {e.Message}");
        }

        return sb.ToString();
    }
}
