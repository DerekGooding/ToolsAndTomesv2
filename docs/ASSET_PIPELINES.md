# Tools and Tomes — AI Asset Pipelines

Design for generating 3D models, materials, VFX, UI art, and audio with **self-hosted AI models**, feeding the Godot 4 (C#) client described in [DESIGN.md §6](DESIGN.md).

> **Status:** design (Nov 2026). Nothing here is built yet. Model licenses, VRAM figures, and prices change often — re-verify each model's license on its official model card before adding it to the approved list (§5).

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
   tools/ToolsAndTomes.Assets  (orchestrator CLI, like the balance runner)
            │  diff content vs. manifest → work list
            ▼
   ┌──────────────┬───────────────┬──────────────┬─────────────┐
   │ Image (ComfyUI API)  3D (TRELLIS service)   Audio (Stable Audio service)
   └──────────────┴───────────────┴──────────────┴─────────────┘
            │  N candidates per asset
            ▼
   Post-process: Blender headless (mesh) · Pillow/ImageMagick (2D) · ffmpeg (audio)
            ▼
   Validate (budgets, sockets, naming, loudness)  →  Review UI (approve / regenerate / reject)
            ▼
   Approved → assets/ (Git LFS) + manifest.json  →  Godot import (.glb / .png / .ogg)
```

- **Orchestrator:** a .NET CLI in `tools/`, reusing Core's content loader. Generators run as local HTTP services (ComfyUI's API, small Python wrappers for 3D and audio models), so the orchestrator doesn't care whether a GPU is local or rented.
- **Work list:** an asset is generated when it has no approved entry, or when the hash of its `assetSpec` + workflow + model version changed.
- **Storage:** approved assets in `assets/` under Git LFS; candidates and intermediates in a scratch store outside the repo.

## 3. Asset spec (content schema)

Added alongside a definition's rules data. Example on a component in `equipment.json`:

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
- **Hookup:** sound banks keyed by `CombatEventType` + action id, so the Godot client plays the right sound from the engine's event stream.

### 4.7 Music
- Same audio pipeline with longer generations for ambient and combat loops; loop-point detection and crossfade in post.
- Adaptive music driven by encounter state (calm / windup-heavy / near defeat).
- Music has the strictest licensing landscape (§5); if no model clears the bar, music is commissioned or licensed instead.

### 4.8 Voice (optional, later)
- Only if the game adds spoken lines; requires a model whose license *and* voice data permit commercial use.

## 5. Approved models (verify before use)

| Use | Candidate | License (as reported, Nov 2026) | Commercial-clean? | VRAM (approx.) |
|---|---|---|---|---|
| Image | **FLUX.1 schnell** | Apache 2.0 | ✅ | ~12 GB quantized, ~24 GB full |
| Image | **SDXL 1.0 base/refiner** | CreativeML Open RAIL++-M (use-based restrictions only) | ✅ (respect use restrictions) | 8–12 GB |
| Image | FLUX.1 dev | Non-commercial | ❌ | — |
| Image | SDXL Turbo | Non-commercial community license | ❌ | — |
| 3D | **TRELLIS.2** (Microsoft) | MIT | ✅ (confirm current repo license) | ~24 GB |
| 3D | **TripoSR** | MIT | ✅ | ~6 GB |
| 3D | Hunyuan3D 2.x | Tencent community license; excludes EU/UK/South Korea | ❌ (territorial restriction) | ~10–29 GB |
| SFX / music | **Stable Audio Open** | Stability Community License: commercial use free under $1M annual revenue | ⚠️ while under the cap; track revenue | consumer GPU |
| Music | MusicGen / AudioGen | Code MIT, **weights CC BY-NC 4.0** | ❌ | — |

Rules:
- A model joins this table only after someone reads its official license and records the date and link in the manifest's model registry.
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

1. **Model registry + license review** (§5) and the containerized generator services.
2. **Style bible** (§4.1): exploration, selection, style LoRA.
3. **Asset spec schema** in Core content + validator checks + manifest format.
4. **3D component pipeline** end to end for one category (Weapon), including Blender cleanup, sockets, and toon materials; assemble a Crest in a Godot test scene.
5. **SFX pipeline** for one action set; wire to the engine's event stream.
6. Review tool; then VFX, icons, remaining categories, music.
