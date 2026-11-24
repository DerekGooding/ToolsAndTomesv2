using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Content;
using ToolsAndTomes.Core.Definitions;

// Headless balance sweep: plays every starter build against the test encounter across many seeds.
//
//   dotnet run --project tools/ToolsAndTomes.Balance -- [--seeds 200] [--beats 300] [--content <dir>] [--build <id>] [--details]
//
// --content loads an alternative content directory (e.g. an edited copy of src/ToolsAndTomes.Core/Content),
// so balance changes can be compared before they are committed.

int seeds = 200, beats = 300;
string? contentDir = null, buildFilter = null;
bool details = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seeds": seeds = int.Parse(args[++i]); break;
        case "--beats": beats = int.Parse(args[++i]); break;
        case "--content": contentDir = args[++i]; break;
        case "--build": buildFilter = args[++i]; break;
        case "--details": details = true; break;
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

var runner = new BalanceRunner(catalog);
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
