using System.Text;
using ToolsAndTomes.Core.Campaign;
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
    int EnemiesDefeated,
    string SurvivingEnemies);

public sealed record MissionResult(
    string BuildId,
    string MissionId,
    int Seed,
    MissionPhase Phase,
    bool TimedOut,
    int EncountersWon,
    int TotalBeats,
    int ComponentsDisabledAtEnd,
    int SamplesEarned);

public sealed record MissionSummary(
    string BuildName,
    string MissionName,
    int Runs,
    int Completed,
    int Failed,
    int TimedOut,
    double AverageEncountersWon,
    double AverageSamples)
{
    public double CompletionRate => Runs == 0 ? 0 : (double)Completed / Runs;
}

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
    public IReadOnlyList<EncounterResult> Run(IEnumerable<CrestConfiguration> builds, int seeds, int maxBeats = 300, int firstSeed = 1, string? encounterId = null)
    {
        var results = new List<EncounterResult>();
        foreach (var build in builds)
        {
            for (int seed = firstSeed; seed < firstSeed + seeds; seed++)
                results.Add(RunOne(build, seed, maxBeats, encounterId));
        }

        return results;
    }

    /// <summary>One fight with a fresh Crest (the test encounter unless <paramref name="encounterId"/> is given).</summary>
    public EncounterResult RunOne(CrestConfiguration build, int seed, int maxBeats, string? encounterId = null)
    {
        var loadout = CrestLoadout.CreateFromConfiguration(build, catalog, "bal");
        var encounter = catalog.Encounters[encounterId ?? EncounterFactory.TestEncounterId];
        var sim = EncounterFactory.CreateEncounter(encounter, loadout, seed, catalog);
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
            sim.Combatants.Count(c => c.Faction == Faction.Enemy && c.IsDefeated),
            string.Join(",", sim.Combatants.Where(c => c.Faction == Faction.Enemy && !c.IsDefeated).Select(c => c.Id)));
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

    // ---- Campaign missions ------------------------------------------------------------------

    public IReadOnlyList<MissionResult> RunMissions(
        IEnumerable<CrestConfiguration> builds, IEnumerable<MissionDefinition> missions, int seeds, int maxBeatsPerEncounter = 300, int firstSeed = 1)
    {
        var results = new List<MissionResult>();
        var missionList = missions.ToList();
        foreach (var build in builds)
            foreach (var mission in missionList)
                for (int seed = firstSeed; seed < firstSeed + seeds; seed++)
                    results.Add(RunMission(build, mission, seed, maxBeatsPerEncounter));

        return results;
    }

    /// <summary>
    /// Plays a whole mission: <see cref="AutoPilot"/> fights; <see cref="AutoRest"/> spends each rest.
    /// An encounter that hits the beat limit ends the mission as a withdrawal (timed out).
    /// </summary>
    public MissionResult RunMission(CrestConfiguration build, MissionDefinition mission, int seed, int maxBeatsPerEncounter)
    {
        var loadout = CrestLoadout.CreateFromConfiguration(build, catalog, "bal");
        var run = MissionRun.Start(mission, loadout, catalog, seed);
        var enemyAI = new EnemyAIController();
        var pilot = new AutoPilot();
        int wins = 0, beats = 0;
        bool timedOut = false;

        while (!run.IsOver)
        {
            if (run.Phase == MissionPhase.Rest)
            {
                AutoRest(run);
                run.ContinueToNextEncounter();
                continue;
            }

            var sim = run.BeginEncounter();
            while (sim.Outcome == EncounterOutcome.InProgress && sim.CurrentBeat < maxBeatsPerEncounter)
            {
                enemyAI.Update(sim);
                pilot.Update(sim, run.Crest);
                sim.StepBeat();
            }

            beats += sim.CurrentBeat;
            if (sim.Outcome == EncounterOutcome.InProgress)
            {
                timedOut = true;
                run.Withdraw();
                break;
            }

            if (sim.Outcome == EncounterOutcome.PlayerVictory)
                wins++;
            run.FinishEncounter();
        }

        return new MissionResult(build.Id, mission.Id, seed, run.Phase, timedOut, wins, beats,
            loadout.Installed.Count(e => e.IsDisabled), run.RewardsEarned.Sum(r => r.Count));
    }

    /// <summary>
    /// A simple rest policy: wait out the rest (summoning fast-iron whenever Motes allow), then repair the most
    /// damaged components, Core first, while fast-iron lasts.
    /// </summary>
    public static void AutoRest(MissionRun run)
    {
        do
        {
            while (run.SquareSummon().Succeeded) { }
        } while (run.Wait().Succeeded);
        while (run.SquareSummon().Succeeded) { }

        while (true)
        {
            var part = run.Crest.Loadout.Installed
                .Where(e => e.CurrentDurability < e.Definition.MaxDurability)
                .OrderByDescending(e => e.Definition.IsCore)
                .ThenByDescending(e => e.Definition.MaxDurability - e.CurrentDurability)
                .ThenBy(e => e.InstanceId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (part is null || !run.Repair(part.InstanceId).Succeeded)
                break;
        }
    }

    public static IReadOnlyList<MissionSummary> SummarizeMissions(IEnumerable<MissionResult> results, GameCatalog catalog) =>
        results
            .GroupBy(r => (r.BuildId, r.MissionId))
            .Select(g => new MissionSummary(
                catalog.StarterConfigurations.TryGetValue(g.Key.BuildId, out var cfg) ? cfg.Name : g.Key.BuildId,
                catalog.Missions[g.Key.MissionId].Name,
                g.Count(),
                g.Count(r => r.Phase == MissionPhase.Completed),
                g.Count(r => r.Phase == MissionPhase.Failed),
                g.Count(r => r.TimedOut),
                g.Average(r => r.EncountersWon),
                g.Average(r => r.SamplesEarned)))
            .ToList();

    public static string FormatMissionTable(IEnumerable<MissionSummary> summaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Build",-24} {"Mission",-15} {"Runs",5} {"Done%",6} {"Fail",5} {"T/O",4} {"Wins",5} {"Samples",8}");
        foreach (var s in summaries)
        {
            sb.AppendLine(
                $"{Truncate(s.BuildName, 24),-24} {Truncate(s.MissionName, 15),-15} {s.Runs,5} {s.CompletionRate,6:P0} {s.Failed,5} {s.TimedOut,4} " +
                $"{s.AverageEncountersWon,5:F1} {s.AverageSamples,8:F1}");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
