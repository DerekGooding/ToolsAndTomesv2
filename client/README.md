# Tools and Tomes — Godot client

The game client, built in **Godot 4.7 (.NET / C#)** with placeholder assets. All rules come from
`src/ToolsAndTomes.Core`, which this project references directly; the client only presents them.

## Run it

1. Install **Godot 4.7.2 .NET** (the "Mono" build) and the **.NET 10 SDK**.
2. Open `client/ToolsAndTomes.Client/project.godot` in Godot and press **Play** (F5),
   or from the repo root:
   ```
   godot --path client/ToolsAndTomes.Client
   ```

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
godot --headless --path client/ToolsAndTomes.Client -- --autoplay
```

## What's placeholder

Crests are assembled from primitives — one per installed component, shaped by category (sphere Core, leg
cylinders, armor plates, weapon arms, defense rings, arcane/utility blocks). Disabled components turn dark.
Real models replace the primitives per component via the asset pipelines in `docs/ASSET_PIPELINES.md`.

## Generated assets

`assets/` holds files that the asset pipeline has approved (see `docs/ASSET_PIPELINES.md` §0). Don't edit them by hand: change `assets/specs.json` at the repo root and regenerate.

- `assets/icons/<contentId>.png`: shown on the action bar.
- `assets/sfx/<actionId>/{windup,impact}_<n>.ogg` and `assets/sfx/event_<CombatEventType>/play_<n>.ogg`: played by `SoundBank` from the combat event stream.

Today every file is a procedural placeholder. Missing files are skipped silently, so the game runs with any subset.
