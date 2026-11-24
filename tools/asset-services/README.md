# Asset generator services

Self-hosted generators for the asset pipeline ([docs/ASSET_PIPELINES.md](../../docs/ASSET_PIPELINES.md)). Each service is a small HTTP server. The .NET orchestrator (`tools/ToolsAndTomes.Assets`) sends it a fully resolved spec and gets back finished, post-processed files. The orchestrator doesn't care whether the GPU is local or rented.

| Service | Port | Backends |
|---|---|---|
| image | 8411 | `procedural-image` (placeholder, no GPU), `sdxl`, `flux-schnell` |
| audio | 8412 | `procedural-audio` (placeholder, no GPU), `stable-audio-open` |

The procedural backends need only the base install. They exist so the whole pipeline (and the game) works before any model is downloaded.

## Setup

Requires [uv](https://docs.astral.sh/uv/) and Python 3.11+.

```powershell
cd tools/asset-services
uv sync                 # base: procedural backends + post-processing (small)
uv sync --extra ai      # AI backends: PyTorch (CUDA 12.8) + diffusers, ~3 GB; model weights download on first use
```

The gated models (`flux-schnell`, `stable-audio-open`) need you to accept their terms on Hugging Face, then sign in once with `uv run hf auth login`. Read the license notes in [assets/models.json](../../assets/models.json) first. Stable Audio Open is free for commercial use only under USD 1M revenue, and only after registering with Stability AI.

## Run

```powershell
uv run python -m asset_services.server --kind image     # http://127.0.0.1:8411
uv run python -m asset_services.server --kind audio     # http://127.0.0.1:8412
```

Then, from the repo root:

```powershell
dotnet run --project tools/ToolsAndTomes.Assets -- health
dotnet run --project tools/ToolsAndTomes.Assets -- generate
```

On a rented GPU, run the same commands there with `--host 0.0.0.0`. Point the orchestrator at it through an SSH tunnel (`ssh -L 8411:localhost:8411 …`) or with `--image-url`. Don't expose the port publicly: the service has no authentication.

## Post-processing (shared by every backend)

- **Icons** (`finish_icon`):
  1. Cut the subject out of the flat background.
  2. Drop specks.
  3. Frame it at 84% of the canvas.
  4. Add the toon outline.
  5. Output a square RGBA PNG.

  Metrics: `coverage`, `subjectFound`, `touchesEdge`, `colors`.
- **Sounds** (`finish_sfx`):
  1. Trim silence below −50 dBFS.
  2. Fade: 5 ms in, 40 ms out.
  3. Normalize loudness to the target (ITU-R BS.1770 via pyloudnorm).
  4. Soft-limit to the peak ceiling.
  5. Encode OGG Vorbis.

  Metrics: `durationSeconds`, `lufs`, `peakDb`.

The orchestrator turns these metrics into review warnings and errors.

## Hardware notes

- **SDXL** needs a GPU that runs it in fp16: RTX 20xx or newer, about 8 GB with CPU offload.
  - Turing cards without tensor cores (GTX 16xx, Quadro T400–T1200) overflow to NaN in fp16, so every image comes out black. The backend detects this at the first step, fails the request, and `health` then reports SDXL as unusable.
  - Measured on a T1000 8 GB (2026-11-24): NaN at every size, and about 39 s/step at 1024² even before the NaN check existed. Use a rented GPU on such cards.
- **FLUX.1 schnell** needs about 24 GB and bfloat16 (Ampere or newer). Use a rented GPU.
- **Stable Audio Open** fits in 8 GB with offload.
