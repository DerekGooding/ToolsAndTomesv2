using System.Net;
using System.Text;

namespace ToolsAndTomes.Assets;

/// <summary>
/// A static review sheet: every asset with candidates waiting, side by side (images, or audio players), with their
/// automatic-check results and the exact `approve` command to copy. Written next to the candidates, outside the repo.
/// </summary>
public static class ReviewPage
{
    public static string Write(AssetProject project, IReadOnlyList<AssetJob> jobs)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Asset Review</title>
            <style>
              :root { --bg:#f4f5f7; --panel:#fff; --ink:#1b1f24; --muted:#5b6470; --line:#d8dde3; --warn:#9a6700; --err:#c62828; --ok:#1b7f3b; --chip:#eef1f4; }
              @media (prefers-color-scheme: dark) { :root { --bg:#14171b; --panel:#1d2126; --ink:#e6e9ed; --muted:#9aa4af; --line:#2e343b; --warn:#e3b341; --err:#ff7b72; --ok:#56d364; --chip:#262b31; } }
              * { box-sizing: border-box; }
              body { margin:0; padding:24px 16px; background:var(--bg); color:var(--ink); font:14px/1.45 system-ui, sans-serif; }
              main { max-width:1200px; margin:0 auto; }
              h1 { font-size:20px; margin:0 0 4px; }
              .sub { color:var(--muted); margin:0 0 20px; }
              section { background:var(--panel); border:1px solid var(--line); border-radius:10px; padding:16px; margin-bottom:16px; }
              h2 { font-size:15px; margin:0; font-family:ui-monospace, monospace; }
              .state { font-size:12px; color:var(--muted); margin-left:8px; font-family:system-ui; font-weight:400; }
              .prompt { color:var(--muted); margin:6px 0 12px; font-size:13px; }
              .grid { display:grid; grid-template-columns:repeat(auto-fill, minmax(180px, 1fr)); gap:12px; }
              .cand { border:1px solid var(--line); border-radius:8px; padding:8px; }
              .cand img { width:100%; aspect-ratio:1; object-fit:contain; display:block;
                background: repeating-conic-gradient(#8881 0 25%, transparent 0 50%) 50% / 16px 16px; border-radius:4px; }
              .cand audio { width:100%; }
              .n { font-weight:600; }
              .m { color:var(--muted); font-size:12px; font-family:ui-monospace, monospace; word-break:break-word; }
              .w { color:var(--warn); font-size:12px; } .e { color:var(--err); font-size:12px; } .okc { color:var(--ok); font-size:12px; }
              code { display:block; background:var(--chip); padding:6px 8px; border-radius:6px; margin-top:10px; font-size:12px; overflow-x:auto; white-space:nowrap; }
            </style></head><body><main>
            """);

        var pending = jobs
            .Select(j => (Job: j, Set: AssetPipeline.LoadCandidates(project, j), State: AssetPlanner.StateOf(project, j)))
            .Where(x => x.Set is not null && (x.State != AssetState.Approved || NewerThanApproval(project, x.Job, x.Set)))
            .ToList();

        html.Append($"<h1>Asset review</h1><p class=\"sub\">{pending.Count} asset(s) waiting. Generated {Enc(DateTime.Now.ToString("g"))}. " +
                    "Run the command under an asset to approve; for sounds list up to the variant count in play order.</p>");

        foreach (var (job, set, state) in pending)
        {
            html.Append($"<section><h2>{Enc(job.AssetId)}<span class=\"state\">{state} · {Enc(set!.Backend)}</span></h2>");
            html.Append($"<p class=\"prompt\">{Enc(set.Prompt)}</p><div class=\"grid\">");
            foreach (var c in set.Candidates)
            {
                string src = new Uri(Path.Combine(project.CandidateDir(job), c.File)).AbsoluteUri;
                html.Append("<div class=\"cand\">");
                html.Append(job.Kind == AssetKind.Icon
                    ? $"<img src=\"{src}\" alt=\"candidate {c.Index}\">"
                    : $"<audio controls preload=\"none\" src=\"{src}\"></audio>");
                html.Append($"<div class=\"n\">#{c.Index} <span class=\"m\">seed {c.Seed}</span></div>");
                html.Append($"<div class=\"m\">{Enc(string.Join(" · ", c.Metrics.Where(kv => kv.Key is not ("width" or "height" or "sampleRate")).Select(kv => $"{kv.Key} {kv.Value}")))}</div>");
                foreach (string e in c.Errors) html.Append($"<div class=\"e\">✕ {Enc(e)}</div>");
                foreach (string w in c.Warnings) html.Append($"<div class=\"w\">! {Enc(w)}</div>");
                if (c.Errors.Count == 0 && c.Warnings.Count == 0) html.Append("<div class=\"okc\">✓ checks passed</div>");
                html.Append("</div>");
            }

            string example = job.Kind == AssetKind.Icon ? "1" : string.Join(",", Enumerable.Range(1, Math.Min(job.Variants, set.Candidates.Count)));
            html.Append($"</div><code>dotnet run --project tools/ToolsAndTomes.Assets -- approve {Enc(job.AssetId)} {example}</code></section>");
        }

        if (pending.Count == 0)
            html.Append("<section>Nothing waiting for review. Run <b>generate</b> first.</section>");
        html.Append("</main></body></html>");

        Directory.CreateDirectory(project.CandidatesRoot);
        string path = Path.Combine(project.CandidatesRoot, "review.html");
        File.WriteAllText(path, html.ToString());
        return path;
    }

    /// <summary>Candidates regenerated (e.g. with --force) after the current approval are up for review again.</summary>
    private static bool NewerThanApproval(AssetProject project, AssetJob job, CandidateSet set) =>
        project.Manifest.Assets.TryGetValue(job.AssetId, out var entry) &&
        DateTimeOffset.TryParse(set.CreatedAt, out var created) &&
        DateTimeOffset.TryParse(entry.ApprovedAt, out var approved) &&
        created > approved;

    private static string Enc(string text) => WebUtility.HtmlEncode(text);
}
