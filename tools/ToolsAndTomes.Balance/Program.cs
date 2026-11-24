using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;

// Headless balance sweep: plays every starter build against the test encounter across many seeds.
//
//   dotnet run --project tools/ToolsAndTomes.Balance -- [--seeds 200] [--beats 300] [--content <dir>] [--build <id>] [--details]
//   ... --missions            play every campaign mission end to end (auto-rest between encounters)
//   ... --mission <id>        play one mission
//   ... --allies <ids|all>    pilots who join the player's lance, comma-separated (e.g. pilot_marrow,pilot_vell)
//
// --content loads an alternative content directory (e.g. an edited copy of src/ToolsAndTomes.Core/Content),
// so balance changes can be compared before they are committed.

int seeds = 200, beats = 300;
string? contentDir = null, buildFilter = null;
bool details = false;
bool missions = false;
bool encounters = false;
string? missionFilter = null;
string? alliesArg = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seeds": seeds = int.Parse(args[++i]); break;
        case "--beats": beats = int.Parse(args[++i]); break;
        case "--content": contentDir = args[++i]; break;
        case "--build": buildFilter = args[++i]; break;
        case "--details": details = true; break;
        case "--missions": missions = true; break;
        case "--mission": missions = true; missionFilter = args[++i]; break;
        case "--encounters": encounters = true; break;
        case "--allies": alliesArg = args[++i]; break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
            return 1;
    }
}

GameCatalog catalog;
try
{
    catalog = contentDir is null ? GameCatalog.Default : ContentSerializer.LoadFromDirectory(contentDir);
}
catch (ContentValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var builds = catalog.StarterConfigurations.Values
    .Where(b => buildFilter is null || b.Id == buildFilter)
    .ToList();
if (builds.Count == 0)
{
    Console.Error.WriteLine($"No build matches '{buildFilter}'.");
    return 1;
}

var allyIds = alliesArg switch
{
    null or "" or "none" => [],
    "all" => catalog.Pilots.Keys.Take(ToolsAndTomes.Core.Campaign.MissionRun.MaxAllies).ToList(),
    _ => alliesArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
};
if (allyIds.FirstOrDefault(id => !catalog.Pilots.ContainsKey(id)) is { } unknownPilot)
{
    Console.Error.WriteLine($"Unknown pilot '{unknownPilot}'. Pilots: {string.Join(", ", catalog.Pilots.Keys)}.");
    return 1;
}
if (allyIds.Count > ToolsAndTomes.Core.Campaign.MissionRun.MaxAllies)
{
    Console.Error.WriteLine($"A lance holds at most {ToolsAndTomes.Core.Campaign.MissionRun.MaxAllies} allies.");
    return 1;
}
if (allyIds.Count > 0)
    Console.WriteLine($"Allies: {string.Join(", ", allyIds.Select(id => catalog.Pilots[id].DisplayName))}");

var runner = new BalanceRunner(catalog, allyIds);

if (encounters)
{
    Console.WriteLine($"Every encounter with a fresh Crest: {seeds} seeds, {beats}-beat limit");
    Console.WriteLine();
    Console.WriteLine($"{"Encounter",-20} {"Build",-24} {"Win%",6} {"Loss%",6} {"T/O%",6} {"Beats",7} {"Lost",5}");
    foreach (var enc in catalog.Encounters.Values)
    {
        foreach (var b in builds)
        {
            var rs = runner.Run([b], seeds, beats, encounterId: enc.Id);
            double n = rs.Count;
            Console.WriteLine($"{enc.Id,-20} {b.Name,-24} {rs.Count(r => r.Outcome == ToolsAndTomes.Core.Simulation.EncounterOutcome.PlayerVictory) / n,6:P0} {rs.Count(r => r.Outcome == ToolsAndTomes.Core.Simulation.EncounterOutcome.PlayerDefeat) / n,6:P0} {rs.Count(r => r.Outcome == ToolsAndTomes.Core.Simulation.EncounterOutcome.InProgress) / n,6:P0} {rs.Average(r => r.Beats),7:F0} {rs.Average(r => r.ComponentsLost),5:F1}");
        }
    }
    return 0;
}

if (missions)
{
    var missionList = catalog.Missions.Values.Where(m => missionFilter is null || m.Id == missionFilter).ToList();
    var missionResults = runner.RunMissions(builds, missionList, seeds, beats);
    Console.WriteLine($"Campaign missions: {seeds} seeds per build and mission, {beats}-beat limit per encounter, content: {contentDir ?? "embedded"}");
    Console.WriteLine();
    Console.Write(BalanceRunner.FormatMissionTable(BalanceRunner.SummarizeMissions(missionResults, catalog)));
    if (details)
    {
        Console.WriteLine();
        foreach (var r in missionResults)
            Console.WriteLine($"{r.BuildId,-22} {r.MissionId,-18} seed {r.Seed,4}: {r.Phase,-10}{(r.TimedOut ? " (timed out)" : "")} wins {r.EncountersWon}  beats {r.TotalBeats}  samples {r.SamplesEarned}");
    }
    return 0;
}
var results = runner.Run(builds, seeds, beats);

Console.WriteLine($"Test encounter: {seeds} seeds per build, {beats}-beat limit, content: {contentDir ?? "embedded"}");
Console.WriteLine();
Console.Write(BalanceRunner.FormatTable(BalanceRunner.Summarize(results, catalog)));

if (details)
{
    Console.WriteLine();
    foreach (var r in results)
        Console.WriteLine($"{r.BuildId,-22} seed {r.Seed,4}: {r.Outcome,-14} beats {r.Beats,4}  lost {r.ComponentsLost}  kills {r.EnemiesDefeated}  alive: {r.SurvivingEnemies}");
}

return 0;
