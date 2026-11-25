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
3. Play, or open the editor, from the repo root:
   ```powershell
   tools/godot.ps1 --path client/ToolsAndTomes.Client       # play
   tools/godot.ps1 -e --path client/ToolsAndTomes.Client    # editor
   ```

The game's own `user://` data (logs today, saves later) still goes to `%APPDATA%\Godot\app_userdata\Tools and Tomes`, as a shipped game's would.

## Controls

| Input | Action |
|---|---|
| Left-click | Select an enemy target / place a point-targeted action |
| Right-click | Move |
| Action bar | Use an action (reactions light up during incoming impacts) |
| Space / `.` | Pause / step one beat while paused |
| Tab | Next target |
| WASD, Q/E, wheel, middle-drag | Pan, rotate, zoom, tilt the camera |
| R | Restart the encounter |

## Smoke test

Runs the fight headless at full speed with the player on autopilot, prints the result, and quits:

```
tools/godot.ps1 --headless --path client/ToolsAndTomes.Client -- --autoplay
```

The fight is the demo's **Border Fort** with the two allied pilots (teal Crests, AI-controlled, status on the HUD's "Lance" line).
- `--encounter <id>` picks another fight (ids in `src/ToolsAndTomes.Core/Content/encounters.json`, e.g. `enc_bm_warlord`).
- `--solo` fights alone, e.g. `-- --autoplay --solo`.

## What's placeholder

Crests are assembled from primitives — one per installed component, shaped by category (sphere Core, leg
cylinders, armor plates, weapon arms, defense rings, arcane/utility blocks). Disabled components turn dark.
Real models replace the primitives per component via the asset pipelines in `docs/ASSET_PIPELINES.md`.

## Generated assets

`assets/` holds files that the asset pipeline has approved (see `docs/ASSET_PIPELINES.md` §0). Don't edit them by hand: change `assets/specs.json` at the repo root and regenerate.

- `assets/icons/<contentId>.png`: shown on the action bar.
- `assets/sfx/<actionId>/{windup,impact}_<n>.ogg` and `assets/sfx/event_<CombatEventType>/play_<n>.ogg`: played by `SoundBank` from the combat event stream.

Today every file is a procedural placeholder. Missing files are skipped silently, so the game runs with any subset.
