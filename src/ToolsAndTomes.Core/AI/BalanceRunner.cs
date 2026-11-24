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
    string SurvivingEnemies,
    int AlliesDown = 0);

public sealed record MissionResult(
    string BuildId,
    string MissionId,
    int Seed,
    MissionPhase Phase,
    bool TimedOut,
    int EncountersWon,
    int TotalBeats,
    int ComponentsDisabledAtEnd,
    int SamplesEarned,
    int AlliesDownAtEnd = 0);

public sealed record MissionSummary(
    string BuildName,
    string MissionName,
    int Runs,
    int Completed,
    int Failed,
    int TimedOut,
    double AverageEncountersWon,
    double AverageSamples,
    double AverageAlliesDown = 0,
    double AverageBeatsPerFight = 0)
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
/// allies on <see cref="AllyAIController"/>, enemies on <see cref="EnemyAIController"/>) and summarizes the
/// results. Deterministic for a given catalog, build list, ally list, and seed range.
/// </summary>
/// <param name="allyPilotIds">Pilots who join every run, in lance order, with their starting assemblies.</param>
public sealed class BalanceRunner(GameCatalog catalog, IReadOnlyList<string>? allyPilotIds = null)
{
    private readonly IReadOnlyList<string> _allyPilotIds = allyPilotIds ?? [];

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
        var allies = EncounterFactory.CreateAllies(_allyPilotIds, catalog, encounter.PlayerStartingMotes);
        var sim = EncounterFactory.CreateEncounter(encounter, loadout, seed, catalog, allies);
        var player = sim.FindCombatant(EncounterFactory.PlayerCombatantId)!;
        var enemyAI = new EnemyAIController();
        var allyAI = new AllyAIController();
        var pilot = new AutoPilot();

        while (sim.Outcome == EncounterOutcome.InProgress && sim.CurrentBeat < maxBeats)
        {
            enemyAI.Update(sim);
            allyAI.Update(sim);
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
            string.Join(",", sim.Combatants.Where(c => c.Faction == Faction.Enemy && !c.IsDefeated).Select(c => c.Id)),
            allies.Count(a => a.IsDefeated));
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
        var first = catalog.Encounters[mission.EncounterIds[0]];
        var run = MissionRun.Start(mission, loadout, catalog, seed,
            allies: EncounterFactory.CreateAllies(_allyPilotIds, catalog, first.PlayerStartingMotes));
        var enemyAI = new EnemyAIController();
        var allyAI = new AllyAIController();
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
                allyAI.Update(sim);
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
            loadout.Installed.Count(e => e.IsDisabled), run.RewardsEarned.Sum(r => r.Count),
            run.Allies.Count(a => a.IsDefeated));
    }

    /// <summary>
    /// A simple rest policy for every Crest in the lance: wait out the rest (summoning fast-iron whenever Motes
    /// allow), then repair across the whole lance in priority order while any fast-iron lasts: disabled Cores
    /// first (to bring downed allies back), then the player's damaged parts (Core first), then the allies'.
    /// A Crest spends its own fast-iron first; the rest of the lance hands over what it needs.
    /// </summary>
    public static void AutoRest(MissionRun run)
    {
        void SummonAll()
        {
            foreach (var crest in run.Lance)
                while (run.SquareSummon(crest.Id).Succeeded) { }
        }

        do SummonAll(); while (run.Wait().Succeeded);
        SummonAll();

        var lance = run.Lance.ToList();
        while (true)
        {
            var next = lance
                .SelectMany((crest, order) => crest.Loadout.Installed
                    .Where(e => e.CurrentDurability < e.Definition.MaxDurability)
                    .Select(e => (Crest: crest, Order: order, Part: e)))
                .OrderByDescending(x => x.Part.Definition.IsCore && x.Part.IsDisabled)
                .ThenBy(x => x.Order)
                .ThenByDescending(x => x.Part.Definition.IsCore)
                .ThenByDescending(x => x.Part.Definition.MaxDurability - x.Part.CurrentDurability)
                .ThenBy(x => x.Part.InstanceId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next.Crest is null)
                break;

            if (next.Crest.TotalFastIron == 0 &&
                lance.FirstOrDefault(c => c != next.Crest && c.TotalFastIron > 0) is { } donor)
                run.GiveFastIron(donor.Id, next.Crest.Id);

            if (!run.Repair(next.Part.InstanceId, next.Crest.Id).Succeeded)
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
                g.Average(r => r.SamplesEarned),
                g.Average(r => r.AlliesDownAtEnd),
                g.Sum(r => r.TotalBeats) / (double)Math.Max(1, g.Sum(r => r.EncountersWon + (r.Phase == MissionPhase.Completed ? 0 : 1)))))
            .ToList();

    public static string FormatMissionTable(IEnumerable<MissionSummary> summaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Build",-24} {"Mission",-15} {"Runs",5} {"Done%",6} {"Fail",5} {"T/O",4} {"Wins",5} {"Samples",8} {"AllyDn",6} {"Beats",6}");
        foreach (var s in summaries)
        {
            sb.AppendLine(
                $"{Truncate(s.BuildName, 24),-24} {Truncate(s.MissionName, 15),-15} {s.Runs,5} {s.CompletionRate,6:P0} {s.Failed,5} {s.TimedOut,4} " +
                $"{s.AverageEncountersWon,5:F1} {s.AverageSamples,8:F1} {s.AverageAlliesDown,6:F1} {s.AverageBeatsPerFight,6:F0}");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
