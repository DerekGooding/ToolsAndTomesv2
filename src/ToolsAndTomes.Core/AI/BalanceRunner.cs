using System.Text;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.AI;

public sealed record EncounterResult(
    string BuildId,
    int Seed,
    EncounterOutcome Outcome,
    int Beats,
    int ComponentsLost,
    int EnemiesDefeated);

public sealed record BuildSummary(
    string BuildId,
    string BuildName,
    int Runs,
    int Wins,
    int Losses,
    int Timeouts,
    double AverageBeats,
    double AverageComponentsLost,
    double AverageEnemiesDefeated)
{
    public double WinRate => Runs == 0 ? 0 : (double)Wins / Runs;
}

/// <summary>
/// Plays the test encounter headlessly for each build across many seeds (player on <see cref="AutoPilot"/>,
/// enemies on <see cref="EnemyAIController"/>) and summarizes the results. Deterministic for a given catalog,
/// build list, and seed range.
/// </summary>
public sealed class BalanceRunner(GameCatalog catalog)
{
    public IReadOnlyList<EncounterResult> Run(IEnumerable<CrestConfiguration> builds, int seeds, int maxBeats = 300, int firstSeed = 1)
    {
        var results = new List<EncounterResult>();
        foreach (var build in builds)
        {
            for (int seed = firstSeed; seed < firstSeed + seeds; seed++)
                results.Add(RunOne(build, seed, maxBeats));
        }

        return results;
    }

    public EncounterResult RunOne(CrestConfiguration build, int seed, int maxBeats)
    {
        var loadout = CrestLoadout.CreateFromConfiguration(build, catalog, "bal");
        var sim = EncounterFactory.CreateTestEncounter(loadout, seed, catalog);
        var player = sim.Combatants.First(c => c.Faction == Faction.Player);
        var enemyAI = new EnemyAIController();
        var pilot = new AutoPilot();

        while (sim.Outcome == EncounterOutcome.InProgress && sim.CurrentBeat < maxBeats)
        {
            enemyAI.Update(sim);
            pilot.Update(sim, player);
            sim.StepBeat();
        }

        return new EncounterResult(
            build.Id,
            seed,
            sim.Outcome,
            sim.CurrentBeat,
            loadout.Installed.Count(e => e.IsDisabled),
            sim.Combatants.Count(c => c.Faction == Faction.Enemy && c.IsDefeated));
    }

    public static IReadOnlyList<BuildSummary> Summarize(IEnumerable<EncounterResult> results, GameCatalog catalog) =>
        results
            .GroupBy(r => r.BuildId)
            .Select(g => new BuildSummary(
                g.Key,
                catalog.StarterConfigurations.TryGetValue(g.Key, out var cfg) ? cfg.Name : g.Key,
                g.Count(),
                g.Count(r => r.Outcome == EncounterOutcome.PlayerVictory),
                g.Count(r => r.Outcome == EncounterOutcome.PlayerDefeat),
                g.Count(r => r.Outcome == EncounterOutcome.InProgress),
                g.Average(r => r.Beats),
                g.Average(r => r.ComponentsLost),
                g.Average(r => r.EnemiesDefeated)))
            .ToList();

    public static string FormatTable(IEnumerable<BuildSummary> summaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Build",-28} {"Runs",5} {"Win%",6} {"W",4} {"L",4} {"T/O",4} {"Beats",7} {"Lost",6} {"Kills",6}");
        foreach (var s in summaries)
        {
            sb.AppendLine(
                $"{Truncate(s.BuildName, 28),-28} {s.Runs,5} {s.WinRate,6:P0} {s.Wins,4} {s.Losses,4} {s.Timeouts,4} " +
                $"{s.AverageBeats,7:F1} {s.AverageComponentsLost,6:F1} {s.AverageEnemiesDefeated,6:F1}");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
