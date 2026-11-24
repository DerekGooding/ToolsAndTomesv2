# Tools and Tomes — AI Asset Pipelines

Design for generating 3D models, materials, VFX, UI art, and audio with **self-hosted AI models**, feeding the Godot 4 (C#) client described in [DESIGN.md §6](DESIGN.md).

> **Status (Nov 2026):** the **image (icons) and audio (SFX) pipelines are built** and running end to end. Procedural placeholder backends fill every slot today. SDXL, FLUX.1 schnell, and Stable Audio Open plug in as real backends. The 3D, VFX, and music pipelines are still design only. Model licenses, VRAM figures, and prices change often: re-verify each license on its official page before changing its status in `assets/models.json` (§5).

## 0. Using it (built pipelines)

```powershell
# 1. Start the generator services (tools/asset-services/README.md)
cd tools/asset-services; uv sync
uv run python -m asset_services.server --kind image    # :8411
uv run python -m asset_services.server --kind audio    # :8412

# 2. From the repo root
dotnet run --project tools/ToolsAndTomes.Assets -- validate         # specs vs. content, palettes, licenses, coverage
dotnet run --project tools/ToolsAndTomes.Assets -- status           # approved / stale / missing / waiting
dotnet run --project tools/ToolsAndTomes.Assets -- generate         # candidates for everything missing or stale + review page
dotnet run --project tools/ToolsAndTomes.Assets -- approve icon/eq_titan 3
dotnet run --project tools/ToolsAndTomes.Assets -- approve sfx/weapon_titan/impact 2,5,1   # up to 3 variants, in play order
dotnet run --project tools/ToolsAndTomes.Assets -- reject icon/eq_titan                    # discard; `generate` makes new ones
```

- `generate --auto-approve` approves the first candidates that pass the checks. Use it for placeholders, never for production art.
- `--filter <text>` limits any command to matching asset ids. `--image-backend sdxl` / `--audio-backend stable-audio-open` override `assets/pipeline.json` for one run.
- **Switching to a real model:** set `imageBackend` / `audioBackend` in `assets/pipeline.json`. Every placeholder becomes *stale*, because the backend is part of each asset's hash. The next `generate` remakes them for review.

| File | Role |
|---|---|
| `assets/specs.json` | What to make: one icon per content id; sound slots per action (`windup`, `impact`) and per engine event (`play`) |
| `assets/style.json` | Style bible v0: named palettes, the palette for each category, prompt templates, negative prompts, and a `version` that marks every asset stale when bumped |
| `assets/models.json` | Model registry: license, link, date checked, status (`approved` / `capped` / `rejected`) |
| `assets/pipeline.json` | Service URLs, active backends, sizes, candidate counts, loudness targets |
| `assets/manifest.json` | Provenance of every approved asset: hash, backend, model, license, prompt, seeds, parameters, metrics, approval time. Written by the tool. |
| `client/ToolsAndTomes.Client/assets/` | Approved files, inside the Godot project: `icons/<id>.png`, `sfx/<key>/<slot>_<n>.ogg` |
| `%LOCALAPPDATA%\ToolsAndTomes\asset-candidates\` | Candidates waiting for review, and `review.html` (outside the repo) |

**In game:**
- The action bar shows `icons/<actionId>.png`.
- `SoundBank` plays `sfx/<actionId>/windup_*` when an action starts and `impact_*` when it resolves. It plays `sfx/event_<CombatEventType>/play_*` for engine events (damage, disable, defeat, Aegis block, interruption, Motes, overheat, and so on).
- It picks a random variant with ±5% pitch, plays at the Crest's position, and plays each sound at most once per beat.
- To make this possible, Core's `CombatEvent` now carries the `ActionId` of the action that produced it.

---

## 1. Principles

1. **Commercial-clean only.** A model may be used *anywhere* in the pipeline — including concepting — only if its license allows commercial use of outputs with no territorial, field-of-use, or audience restrictions that could apply to us. Revenue-capped licenses are allowed only while we are under the cap, and are tracked.
2. **Self-hosted.** Models run on hardware we control: a local workstation or rented cloud GPUs running the same open models. No third-party generation APIs, so prompts, outputs, and the style stay ours.
3. **Content drives assets.** Every component, action, consumable, enemy, and encounter in `src/ToolsAndTomes.Core/Content/*.json` can carry an `assetSpec`. The pipeline generates whatever is missing or stale — the same data-driven, validated approach as the rules.
4. **Reproducible.** Every shipped asset has a manifest entry recording the model, version, workflow, prompt, and seed, so it can be regenerated, audited, or disclosed.
5. **Human-gated.** Generators produce candidates; a person approves one. Hero assets get artist touch-up. AI is the fast first draft, not the final say.
6. **Toon / cel-shaded art direction.** Readable silhouettes, flat color regions, outlines. This deliberately lowers the bar for AI mesh and texture quality: geometry noise and texture detail matter far less than shape and palette.

## 2. Architecture

```
Content JSON (+ assetSpec)          Style bible (palettes, style LoRA, reference sheets)
            │                                   │
            ▼                                   ▼
   tools/ToolsAndTomes.Assets  (orchestrator CLI, like the balance runner)   [built]
            │  diff content vs. manifest → work list
            ▼
   ┌──────────────┬───────────────┬──────────────┬─────────────┐
   │ Image service [built]   3D (TRELLIS service)   Audio service [built]
   └──────────────┴───────────────┴──────────────┴─────────────┘
            │  N candidates per asset
            ▼
   Post-process: Blender headless (mesh) · Pillow/NumPy (2D) [built] · NumPy/pyloudnorm (audio) [built]
            ▼
   Validate (budgets, sockets, naming, loudness)  →  Review UI (approve / regenerate / reject)
            ▼
   Approved → client/ToolsAndTomes.Client/assets/ + assets/manifest.json  →  Godot import (.glb / .png / .ogg)
```

- **Orchestrator:** a .NET CLI in `tools/`, reusing Core's content loader.
- **Generators:** small Python HTTP services (`tools/asset-services`) that call Hugging Face diffusers directly, so the orchestrator doesn't care whether a GPU is local or rented.
  - Finished files travel in the response body.
  - We chose plain diffusers over ComfyUI: one dependency set, nodes-as-code we can test, and no workflow JSON to version. ComfyUI remains an option for style-bible exploration by hand.
- **Work list:** an asset is generated when it has no approved entry, or when its hash changed.
  - The hash covers the resolved prompt, negative prompt, palette, seed, parameters, backend, model source, and style version.
- **Storage:**
  - Approved files live inside the Godot project, so Godot imports and exports them. The `.import` sidecars are committed.
  - Candidates live in a scratch store outside the repo.
  - Git LFS is not enabled yet: today's placeholders total about 4 MB. Turn on LFS for `*.png`, `*.ogg`, and `*.glb` under the client's `assets/` before production art lands.

## 3. Asset spec (content schema)

> **As built:** specs live in `assets/specs.json`, keyed by content id, not inside Core's rules JSON.
> - This keeps the rules content tabletop-pure, and art iteration never touches files the rules tests depend on.
> - The orchestrator's `validate` does the checks proposed below. Every spec must name real content and a known palette. Every component, action, consumable, and material needs an icon, and every action needs an impact sound.
> - The shape below remains the target for 3D components (sockets, triangle budgets).

Original proposal, inline on a component in `equipment.json`:

```json
{
  "id": "eq_titan",
  "name": "Titan Hammer",
  "category": "Weapon",
  "assetSpec": {
    "kind": "component",
    "prompt": "massive two-handed war hammer arm for a walking arcane mech, riveted iron head, glowing rune band",
    "styleTags": ["arcane-industrial", "chunky"],
    "palette": "iron-brass-rune",
    "socket": "arm_mount",
    "scale": "large",
    "triBudget": 4000,
    "seed": 18273,
    "sfx": { "windup": "heavy servo whine rising", "impact": "crushing metal slam, short" }
  }
}
```

- **Category templates** (per `ComponentCategory` + `socket`) define bounding size, pivot, attachment sockets, triangle budget, and texture size, so a generated part always fits the modular Crest.
- Actions carry VFX/SFX specs keyed by phase (`windup`, `impact`, `recovery`) and by `TelegraphKind`.
- The `ContentValidator` gains asset checks: spec present for shipped content, budgets within category limits, socket known.

## 4. Pipelines

### 4.1 Style bible (first, once)
- **Goal:** a consistent toon look before any production asset.
- **Tools:** image model (§5) in ComfyUI → hundreds of exploratory sheets → art lead selects ~40 references → train a **style LoRA** on approved, commercially-clean outputs.
- **Outputs:** palettes (named, e.g. `iron-brass-rune`), outline/shading rules, silhouette guidelines per component category, the style LoRA.

### 4.2 3D components (the core pipeline)
1. **Reference image:** image model + style LoRA renders the part from the spec on a neutral background, front 3/4 view, plain lighting (cel art suits image-to-3D well).
2. **Image-to-3D:** TRELLIS.2 (preferred) or TripoSR (fast/low-VRAM fallback) → raw mesh.
3. **Blender headless cleanup** (`tools/blender/clean_component.py`):
   - remesh/decimate to the category triangle budget; merge loose geometry;
   - normalize scale and orientation to the category template; set pivot;
   - **place attachment sockets** (empties) from the category template, so parts snap onto a Crest;
   - generate LODs; auto-UV (smart project).
4. **Toon materials (no AI texturing needed):** bake the reference's flat color regions onto the mesh as **vertex colors / material IDs**, map them to the named palette, add a Godot toon shader + outline. Damage states are shader parameters (scorch, cracks), not separate textures.
5. **Export** `.glb` + import settings for Godot.

*Why this works for us:* Crests are assemblies of rigid mechanical parts — no organic skinning — and the toon style hides mesh imperfections. Hero parts (Cores, signature weapons) get artist touch-up in Blender.

### 4.3 Animation (mostly procedural, not AI)
- Rigid parts + sockets mean Crests animate procedurally in Godot: IK legs from locomotion speed, recoil and servo motion keyed to action phases, physics "dead weight" when a component is disabled.
- Motion presets per category (`Weapon.melee.swing`, `Mobility.legs.stride`) driven by the beat timeline the engine already emits.
- AI motion generation is out of scope unless humanoid characters are added later.

### 4.4 VFX
- **Flipbooks and masks:** image model renders toon effect frames (blasts, Aegis shimmer, fast-iron shaping, Mote motes) → packed into sprite sheets.
- **Godot particle presets** per `TelegraphKind` and per action, parameterized by palette.
- Telegraph readability rules (shape + color per telegraph kind) come from the style bible, not the generator.

### 4.5 UI art and icons
- Component, consumable, and material icons from the image model with a fixed icon template (silhouette on a palette swatch) → background removal, outline pass, size variants.
- Research Desk material illustrations, mission cards, and Crest Bay backdrops use the same flow at larger sizes.

### 4.6 Sound effects
- **Tool:** a commercially-usable text-to-audio model (§5), prompted from each action's `sfx` spec per phase.
- **Post:** ffmpeg trim/fade, loudness normalize (e.g. −16 LUFS for SFX), generate 3–5 variants per event for variety, export `.ogg`.
- **Hookup [built]:** sound banks keyed by `CombatEventType` + action id, so the Godot client plays the right sound from the engine's event stream (see §0).
- **As built:**
  - Post-processing uses NumPy and pyloudnorm instead of ffmpeg: no extra install, and the same code runs locally and on a rented GPU.
  - Loudness target: −16 LUFS, −1 dBFS soft-limited peak.
  - Each slot ships 3 variants, picked from 6 candidates.

### 4.7 Music
- Same audio pipeline with longer generations for ambient and combat loops; loop-point detection and crossfade in post.
- Adaptive music driven by encounter state (calm / windup-heavy / near defeat).
- Music has the strictest licensing landscape (§5); if no model clears the bar, music is commissioned or licensed instead.

### 4.8 Voice (optional, later)
- Only if the game adds spoken lines; requires a model whose license *and* voice data permit commercial use.

## 5. Approved models (verify before use)

| Use | Candidate | License (as reported, Nov 2026) | Commercial-clean? | VRAM (approx.) |
|---|---|---|---|---|
| Image | **FLUX.1 schnell** | Apache 2.0 (checked 2026-11-24) | ✅; gated download (accept terms, sign in) | ~24 GB, bf16; ~34 GB download |
| Image | **SDXL 1.0 base** + SDXL-VAE-FP16-Fix | CreativeML Open RAIL++-M; VAE MIT (checked 2026-11-24) | ✅ (respect use restrictions); not gated | 8 GB with offload; ~7 GB download |
| Image | FLUX.1 dev | Non-commercial | ❌ | — |
| Image | SDXL Turbo | Non-commercial community license | ❌ | — |
| 3D | **TRELLIS.2** (Microsoft) | MIT | ✅ (confirm current repo license) | ~24 GB |
| 3D | **TripoSR** | MIT | ✅ | ~6 GB |
| 3D | Hunyuan3D 2.x | Tencent community license; excludes EU/UK/South Korea | ❌ (territorial restriction) | ~10–29 GB |
| SFX / music | **Stable Audio Open 1.0** | Stability AI Community License (checked 2026-11-24) | ⚠️ while under $1M annual revenue | 8 GB with offload; ~5 GB download |
| Music | MusicGen / AudioGen | Code MIT, **weights CC BY-NC 4.0** | ❌ | — |

License notes from the 2026-11-24 check:
- **SDXL:** the model card says "intended for research purposes", but the license grants commercial rights and the license text governs.
- **Stable Audio Open:**
  - Commercial use requires registering with Stability AI.
  - Shipping requires the license copy, a Notice file, and a prominent "Powered by Stability AI".
  - Outputs may not be used to train foundational generative models.
  - Above $1M revenue the license terminates unless Stability grants an enterprise license.
- **Stable Audio Open Small:** same license, but not diffusers-ready (stable-audio-tools only). Not wired up.

Rules:
- A model joins this table only after someone reads its official license and records the date and link in the model registry (`assets/models.json`). The orchestrator refuses any backend whose status there isn't `approved` or `capped`.
- Fine-tuned derivatives (our style LoRA) inherit the base model's license.
- ⚠️ models need a review trigger: if revenue approaches a cap, switch models or buy an enterprise license before release.

## 6. Hardware and compute

**Minimum to develop the pipeline (local):** one 12–16 GB GPU, 32 GB RAM, 1 TB NVMe. Runs SDXL / FLUX schnell (quantized), TripoSR, audio, and Blender post-processing — enough to build and test every stage.

**Recommended for production quality:** a 24 GB GPU (TRELLIS.2, full-precision FLUX), 64 GB RAM, 2 TB NVMe.

**Rent vs. buy:**
- Generation is bursty (big batches when content lands), so **rent first**. Marketplace and specialist GPU clouds are far cheaper than AWS for this: as of late 2026 an RTX 4090 (24 GB) runs roughly **$0.34–0.89/hr** and an L40S (48 GB) roughly **$0.79–1.09/hr**, with interruptible capacity lower still. Prices move constantly — check live pricing.
- Renting still counts as self-hosted: the same open models, our containers, our data.
- A local 24 GB card pays for itself after a few thousand GPU-hours; revisit once monthly usage is known.
- Package every generator as a container image so the same workflow runs on a local machine or a rented GPU unchanged.

## 7. Validation and review

- **Automatic checks:** triangle and texture budgets per category, sockets present and named, pivot and scale within tolerance, palette conformance, audio loudness and duration, naming rules.
- **Review tool:** shows N candidates per asset side by side (3D turntable, audio player), with approve / regenerate (new seed) / edit-prompt / reject. Decisions are written to the manifest.
- **In-game check:** approved components are auto-assembled onto the starter Crests in a Godot test scene for a visual pass.

## 8. Legal and compliance

- **Licenses:** only §5-approved models; the model registry records each license text link and the date checked.
- **Steam:** Steam requires disclosing AI-generated content at submission; the manifest provides exactly what was generated, how, and with what human involvement.
- **Copyright:** purely AI-generated output may not be protectable in some jurisdictions; human selection, editing, and composition strengthen protection. Get legal advice before launch.
- **Training data:** style LoRAs are trained only on our own approved outputs or commissioned art we own.

## 9. Build order

1. ✅ **Model registry + license review** (§5) and the generator services. Containers still to do; the services run under uv today.
2. ◐ **Style bible** (§4.1): v0 palettes and prompt templates are in `assets/style.json`. Still to do: exploration sheets, art-lead selection, and the style LoRA.
3. ✅ **Asset spec schema**, validation, and manifest format (in `assets/`, see §3).
4. **3D component pipeline** end to end for one category (Weapon), including Blender cleanup, sockets, and toon materials; assemble a Crest in a Godot test scene.
5. ✅ **SFX pipeline** for every action and the key engine events, wired to the engine's event stream.
6. ◐ Review tool (static HTML review page plus CLI approve/reject) and icons are done. Still to do: VFX, the remaining categories, music.

**Next for image/audio:**
1. Run SDXL over the icon set on a rented GPU and review it. The local T1000 can't: it produces NaN (black images) in fp16, see `tools/asset-services/README.md`. Budget about 52 icons × 4 candidates at a few seconds each on an RTX 4090 or L4-class GPU.
2. Build the style bible from the best results and train the LoRA.
3. Accept the Stable Audio Open terms, register with Stability, and run it over the SFX set.
