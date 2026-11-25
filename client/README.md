# Tools and Tomes — Godot client

The game client, built in **Godot 4.7 (.NET / C#)** with placeholder assets. All rules come from
`src/ToolsAndTomes.Core`, which this project references directly; the client only presents them.

## Run it

1. Install the **.NET 10 SDK**.
2. Get the project's own copy of **Godot 4.7.2 .NET**. It is portable and lives in `tools/godot/`, which git ignores, so deleting the repo removes it. From the repo root:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/get-godot.ps1
   ```
   The script downloads the official build, checks it against the release's SHA-512 sums, and turns on self-contained mode (`_sc_`). In self-contained mode the editor keeps its settings and caches in `tools/godot/editor_data/`, not `%APPDATA%`.
3. Build the C# (after code changes; the editor builds for you when you press Play), then play or open the editor, from the repo root:
   ```powershell
   dotnet build client/ToolsAndTomes.Client
   tools/godot.ps1 --path client/ToolsAndTomes.Client       # play
   tools/godot.ps1 -e --path client/ToolsAndTomes.Client    # editor (then F5)
   ```
   If PowerShell blocks the script, prefix the commands with `powershell -ExecutionPolicy Bypass -File`.

Godot's own `user://` data (logs) goes to `%APPDATA%\Godot\app_userdata\Tools and Tomes`, as a shipped game's would.

## The game

The main scene (`scenes/Main.tscn`, `GameRoot`) runs the campaign:

**Crest Bay → Mission Select → fight → rest in the field → fight → … → Crest Bay**

- **Crest Bay:**
  - Pick your Crest, and see its stats and Damage Order.
  - Choose your lance (up to two allied pilots).
  - Research recovered samples.
  - Start, resume, or withdraw from a mission, or play a quick battle.
- **Mission Select:** every mission, with its fights, its completion bonus, and what unlocks it.
- **Fight:** the 3D battle. When it's decided, a result panel shows the samples recovered, which allies are down, and a Continue button.
- **Rest:** switch between the lance's Crests. Wait for Motes, cast Square Summon, repair the selected component, and hand fast-iron to an ally. Then continue or withdraw.

The campaign is `CampaignSession` in Core, so the Godot client and the WPF tool (`dotnet run --project src/ToolsAndTomes.App`) share one profile: `%LOCALAPPDATA%\ToolsAndTomes\profile.json`. A mission is saved between fights. Quit at any point and the game resumes at the rest, or at the start of the interrupted fight.

**Not in the Godot client yet** (use the WPF tool for now):
- refitting components and the Damage Order;
- fabricating parts;
- shaping fast-iron and using cargo during a rest.

## Controls

| Input | Action |
|---|---|
| Left-click | Select an enemy target / place a point-targeted action |
| Right-click | Move |
| Action bar | Use an action (reactions light up during incoming impacts) |
| Space / `.` | Pause / step one beat while paused |
| Tab | Next target |
| WASD, Q/E, wheel, middle-drag | Pan, rotate, zoom, tilt the camera |
| R | Restart the fight (quick battles only) |

## Options and smoke tests

Options go after `--`, e.g. `tools/godot.ps1 --path client/ToolsAndTomes.Client -- --encounter enc_bm_warlord`.

| Option | Effect |
|---|---|
| `--encounter <id>` | Open straight into a quick battle (ids in `src/ToolsAndTomes.Core/Content/encounters.json`; default `enc_bm_border_fort`) |
| `--solo` | Quick battle without allies |
| `--autoplay` | Quick battle on autopilot at full speed; prints `AUTOPLAY RESULT` and quits |
| `--campaign-autoplay [missionId]` | Plays through the real screens on autopilot with a throwaway profile: the given mission, or the whole demo region in order until a mission fails. Prints a `CAMPAIGN AUTOPLAY` line per mission. Exit code 1 if a mission failed. |
| `--profile <path>` | Use another profile file |
| `--beats-per-frame <n>`, `--hold <s>` | Slow autoplay down and linger on each screen, for recordings |

```powershell
tools/godot.ps1 --headless --path client/ToolsAndTomes.Client -- --autoplay
tools/godot.ps1 --headless --path client/ToolsAndTomes.Client -- --campaign-autoplay
# Record frames to look at (needs a window, not --headless):
tools/godot.ps1 --path client/ToolsAndTomes.Client --write-movie out/f.png --fixed-fps 2 -- --campaign-autoplay mis_first_patrol --beats-per-frame 40 --hold 1
```

The lance's allied pilots are the teal Crests. They're AI-controlled, and their status is on the HUD's "Lance" line.

## What's placeholder

Crests are assembled from primitives — one per installed component, shaped by category (sphere Core, leg
cylinders, armor plates, weapon arms, defense rings, arcane/utility blocks). Disabled components turn dark.
Real models replace the primitives per component via the asset pipelines in `docs/ASSET_PIPELINES.md`.

## Generated assets

`assets/` holds files that the asset pipeline has approved (see `docs/ASSET_PIPELINES.md` §0). Don't edit them by hand: change `assets/specs.json` at the repo root and regenerate.

- `assets/icons/<contentId>.png`: shown on the action bar.
- `assets/sfx/<actionId>/{windup,impact}_<n>.ogg` and `assets/sfx/event_<CombatEventType>/play_<n>.ogg`: played by `SoundBank` from the combat event stream.

Today every file is a procedural placeholder. Missing files are skipped silently, so the game runs with any subset.
