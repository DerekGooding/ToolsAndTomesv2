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
  - Open the Workshop for your Crest or a pilot's.
  - Start, resume, or withdraw from a mission, or play a quick battle.
- **Workshop:** refit any Crest, yours or an allied pilot's, between missions:
  - rename it, or save it as a new assembly;
  - reorder the Damage Order; install and uninstall parts; move a part straight to another Crest;
  - fabricate known blueprints into spares;
  - load cargo;
  - add, reorder, and remove reaction rules;
  - repair for free;
  - reset all progress (asks first).
- **Mission Select:** every mission, with its fights, its completion bonus, and what unlocks it.
- **Fight:** the 3D battle, with:
  - an inspector at the top left: the timeline of who is winding up and when each hit lands, then your Crest's and your target's parts;
  - a **Field ▾** menu by the action bar: shape ammo, consumables, or components, or use cargo, mid-fight;
  - a **Withdraw** button at the top right (it asks first).

  When the fight is decided, a result panel shows the samples recovered, which allies are down, and a Continue button.
- **Feel:**
  - The camera is a mid-distance third-person boom. It starts behind your Crest facing the enemy, follows you while leaning toward your target, and eases smoothly.
  - Struck parts flash and the body recoils. Damage, saves, evasions, Aegis blocks, interruptions, and disabled parts float up.
  - Breaking parts and falling Crests burst, and defeated Crests slump.
  - The camera shakes with the size of the hit, hardest on your own Crest. Big hits briefly freeze the beat clock (hit-stop).
  - An enemy windup aimed at your lance draws a ring around its target that fills, amber to red, as the strike approaches. Area attacks show their blast radius, and the impact beat flashes.
  - All of this is presentation only; the rules don't change.
- **Rest:** switch between the lance's Crests. Wait for Motes, cast Square Summon, repair the selected component, hand fast-iron to an ally, and shape fast-iron or use cargo. Then continue or withdraw.

The campaign is `CampaignSession` in Core, so the Godot client and the WPF tool (`dotnet run --project src/ToolsAndTomes.App`) share one profile: `%LOCALAPPDATA%\ToolsAndTomes\profile.json`. A mission is saved between fights. Quit at any point and the game resumes at the rest, or at the start of the interrupted fight.

Everything the WPF tool does is now in the Godot client too. The WPF tool remains a 2D debugging and balancing view.

## Controls

| Input | Action |
|---|---|
| Left-click | Select an enemy target / place a point-targeted action / pick yourself or an ally for Ward Ally or Quick Mend |
| Right-click | Move |
| Action bar | Use an action: weapons, then tome spells (purple), then core spells (grey). Reactions light up during incoming impacts. |
| Field ▾ | Shape fast-iron, use cargo, or swap a stowed handheld (Weapon Harness) into a hand |

Terrain: grey stone (pillars, boulders, walls) blocks movement and shots; brown rubble and barricades only block movement, and give +1 protection die to a Crest right behind them. Moves path around obstacles automatically. Mortars, Storm Field, and Gravity Crush arc over walls. Push, Pull, and the Repulsor slam targets into stone for extra damage.
| Space / `.` | Pause / step one beat while paused |
| Tab | Next target |
| WASD, Q/E, wheel, middle-drag | Pan (takes manual control), rotate, zoom, orbit/tilt the camera |
| F / Home | Camera follows your Crest again |
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
| `--open workshop\|missions\|bay` | Start on that screen |
| `--beats-per-frame <n>`, `--hold <s>` | Autoplay speed (`0` = real time, with hit-stop) and lingering on each screen, for recordings |

```powershell
tools/godot.ps1 --headless --path client/ToolsAndTomes.Client -- --autoplay
tools/godot.ps1 --headless --path client/ToolsAndTomes.Client -- --campaign-autoplay
# Record frames to look at (needs a window, not --headless):
tools/godot.ps1 --path client/ToolsAndTomes.Client --write-movie out/f.png --fixed-fps 2 -- --campaign-autoplay mis_first_patrol --beats-per-frame 40 --hold 1
tools/godot.ps1 --path client/ToolsAndTomes.Client --write-movie out/f.png --fixed-fps 10 --quit-after 300 -- --autoplay --beats-per-frame 0   # a fight in real time
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
