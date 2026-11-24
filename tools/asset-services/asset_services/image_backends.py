"""Image backends: procedural placeholders, SDXL, and FLUX.1 schnell. All share `finish_icon` post-processing."""

from __future__ import annotations

import io
import math
import random

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

from .common import Backend, Output, hex_to_rgb, missing_modules, torch_device_report

AI_MODULES = ("torch", "diffusers", "transformers", "accelerate")


# --------------------------------------------------------------------------------------------- post-processing

def finish_icon(image: Image.Image, size: int, tolerance: float = 38.0) -> tuple[bytes, dict]:
    """Cut the subject out of its flat background, frame it, add the toon outline, and encode a square RGBA PNG.

    The background is whatever touches the border and is close to the median border color, so it works for the
    style bible's flat grey background whatever its exact shade. Returns the PNG and validation metrics.
    """
    rgb = np.asarray(image.convert("RGB")).astype(np.float32)
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    background = np.median(border, axis=0)

    close = np.sqrt(((rgb - background) ** 2).sum(axis=2)) < tolerance
    labels, _ = ndimage.label(close)
    edge_labels = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
    subject = ~np.isin(labels, edge_labels[edge_labels > 0])

    # Drop specks: keep components holding at least 1% of the subject area.
    parts, count = ndimage.label(subject)
    if count:
        areas = ndimage.sum(subject, parts, index=range(1, count + 1))
        keep = [i + 1 for i, a in enumerate(areas) if a >= 0.01 * max(areas.max(), 1)]
        subject = np.isin(parts, keep)

    metrics: dict = {"width": size, "height": size}
    if subject.sum() < 0.005 * h * w:
        # Nothing separable from the background: keep the whole frame and flag it for review.
        metrics.update(coverage=1.0, subjectFound=False, touchesEdge=True)
        out = image.convert("RGBA").resize((size, size), Image.LANCZOS)
        return _png(out), metrics

    ys, xs = np.nonzero(subject)
    touches_edge = bool(ys.min() == 0 or xs.min() == 0 or ys.max() == h - 1 or xs.max() == w - 1)
    top, bottom, left, right = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1

    rgba = np.dstack([rgb, subject.astype(np.float32) * 255]).astype(np.uint8)
    cut = Image.fromarray(rgba[top:bottom, left:right], "RGBA")

    # Work at 2x, then downsample for an anti-aliased outline.
    work = size * 2
    inner = int(work * 0.84)
    scale = inner / max(cut.width, cut.height)
    cut = cut.resize((max(1, round(cut.width * scale)), max(1, round(cut.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (work, work), (0, 0, 0, 0))
    canvas.alpha_composite(cut, ((work - cut.width) // 2, (work - cut.height) // 2))

    alpha = np.asarray(canvas)[:, :, 3] > 127
    radius = max(2, work // 64)
    disk = _disk(radius)
    outline = ndimage.binary_dilation(alpha, structure=disk) & ~alpha
    pixels = np.asarray(canvas).copy()
    pixels[outline] = (12, 12, 16, 255)
    pixels[alpha, 3] = 255
    final = Image.fromarray(pixels, "RGBA").resize((size, size), Image.LANCZOS)

    final_alpha = np.asarray(final)[:, :, 3] > 127
    metrics.update(
        coverage=round(float(final_alpha.mean()), 3),
        subjectFound=True,
        touchesEdge=touches_edge,
        colors=_distinct_colors(final),
    )
    return _png(final), metrics


def _disk(radius: int) -> np.ndarray:
    y, x = np.ogrid[-radius:radius + 1, -radius:radius + 1]
    return x * x + y * y <= radius * radius


def _distinct_colors(image: Image.Image) -> int:
    small = image.convert("RGB").resize((64, 64)).quantize(colors=32)
    return len(small.getcolors() or [])


def _png(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


# --------------------------------------------------------------------------------------------- procedural

class ProceduralImage(Backend):
    """Placeholder icons: a seeded toon shape in the palette, picked from keywords in the prompt."""

    model_id = "procedural-image@1"
    placeholder = True

    FAMILIES = {
        "bar": ("sword", "blade", "edge", "slash", "pistol", "barrel", "nozzle", "hammer", "streak", "sprayer"),
        "crystal": ("crystal", "shard", "glass", "ore", "ingot", "capacitor"),
        "shield": ("shield", "ward", "barrier", "plate", "armor", "talisman", "disc"),
        "gear": ("gear", "core", "engine", "furnace", "drive", "device", "emitter", "ring", "regulator", "radiator"),
        "pair": ("legs", "pair", "jets"),
        "burst": ("explosion", "burst", "blast", "fire", "flame", "wave", "splash", "salvo", "shockwave", "bomb"),
    }

    def generate(self, prompt, negative_prompt, seed, count, palette, params):
        size = int(params.get("size", 256))
        background = params.get("background", "#D0D0D0")
        colors = [hex_to_rgb(c) for c in (palette or ["#5A6270", "#B8873B", "#E2C27A", "#3FE0D0"])]
        family = self._family(prompt)

        outputs = []
        for i in range(count):
            rng = random.Random(seed + i)
            image = Image.new("RGB", (512, 512), hex_to_rgb(background))
            self._draw(ImageDraw.Draw(image), family, rng, colors)
            data, metrics = finish_icon(image, size)
            metrics["family"] = family
            outputs.append(Output(seed + i, "png", data, metrics))
        return outputs

    def _family(self, prompt: str) -> str:
        text = prompt.lower()
        for family, words in self.FAMILIES.items():
            if any(w in text for w in words):
                return family
        return "blob"

    @staticmethod
    def _draw(draw: ImageDraw.ImageDraw, family: str, rng: random.Random, colors: list[tuple[int, int, int]]) -> None:
        ink = (12, 12, 16)
        main, second, light, accent = (colors + colors)[:4]
        cx, cy = 256 + rng.randint(-12, 12), 256 + rng.randint(-12, 12)

        def poly(points, fill, width=10):
            draw.polygon(points, fill=fill, outline=ink, width=width)

        def blob(radius, lumps, wobble, fill, cxo=0, cyo=0):
            n = 48
            phase = [rng.uniform(0, math.tau) for _ in range(lumps)]
            pts = []
            for k in range(n):
                a = math.tau * k / n
                r = radius * (1 + sum(wobble * math.sin(a * (j + 2) + phase[j]) for j in range(lumps)) / lumps)
                pts.append((cx + cxo + r * math.cos(a), cy + cyo + r * math.sin(a)))
            poly(pts, fill)

        if family == "bar":
            angle = math.radians(rng.choice([-45, -40, -50, 45]))
            length, width = rng.randint(300, 380), rng.randint(56, 84)
            dx, dy = math.cos(angle), math.sin(angle)
            px, py = -dy, dx
            hl, hw = length / 2, width / 2
            body = [(cx - dx * hl + px * hw, cy - dy * hl + py * hw), (cx + dx * hl + px * hw * 0.6, cy + dy * hl + py * hw * 0.6),
                    (cx + dx * (hl + 30), cy + dy * (hl + 30)), (cx + dx * hl - px * hw * 0.6, cy + dy * hl - py * hw * 0.6),
                    (cx - dx * hl - px * hw, cy - dy * hl - py * hw)]
            poly(body, main)
            gx, gy = cx - dx * hl * 0.55, cy - dy * hl * 0.55
            poly([(gx + px * hw * 2, gy + py * hw * 2), (gx + dx * 26 + px * hw * 2, gy + dy * 26 + py * hw * 2),
                  (gx + dx * 26 - px * hw * 2, gy + dy * 26 - py * hw * 2), (gx - px * hw * 2, gy - py * hw * 2)], second)
            draw.line([(cx - dx * hl * 0.3, cy - dy * hl * 0.3), (cx + dx * hl * 0.8, cy + dy * hl * 0.8)], fill=light, width=10)
        elif family == "crystal":
            top, side = rng.randint(170, 210), rng.randint(95, 130)
            pts = [(cx, cy - top), (cx + side, cy - top * 0.2), (cx + side * 0.6, cy + top * 0.8),
                   (cx - side * 0.6, cy + top * 0.8), (cx - side, cy - top * 0.2)]
            poly(pts, main)
            poly([(cx, cy - top), (cx + side * 0.35, cy - top * 0.1), (cx, cy + top * 0.8), (cx - side * 0.35, cy - top * 0.1)], light, 6)
            draw.ellipse([cx - 18, cy - 18, cx + 18, cy + 18], fill=accent, outline=ink, width=5)
        elif family == "shield":
            wdt, top, bottom = rng.randint(150, 180), rng.randint(150, 175), rng.randint(185, 215)
            pts = [(cx - wdt, cy - top), (cx + wdt, cy - top), (cx + wdt, cy + 10), (cx + wdt * 0.55, cy + bottom * 0.75),
                   (cx, cy + bottom), (cx - wdt * 0.55, cy + bottom * 0.75), (cx - wdt, cy + 10)]
            poly(pts, main, 12)
            poly([(cx - wdt * 0.55, cy - top * 0.6), (cx + wdt * 0.55, cy - top * 0.6), (cx + wdt * 0.5, cy + 20),
                  (cx, cy + bottom * 0.6), (cx - wdt * 0.5, cy + 20)], second, 8)
            draw.ellipse([cx - 34, cy - 54, cx + 34, cy + 14], fill=accent, outline=ink, width=6)
        elif family == "gear":
            teeth, outer, inner = rng.choice([8, 10, 12]), rng.randint(185, 205), rng.randint(140, 160)
            pts = []
            for k in range(teeth * 2):
                a0 = math.tau * k / (teeth * 2)
                r = outer if k % 2 == 0 else inner
                pts += [(cx + r * math.cos(a0 - 0.12), cy + r * math.sin(a0 - 0.12)), (cx + r * math.cos(a0 + 0.12), cy + r * math.sin(a0 + 0.12))]
            poly(pts, main)
            draw.ellipse([cx - 95, cy - 95, cx + 95, cy + 95], fill=second, outline=ink, width=10)
            draw.ellipse([cx - 45, cy - 45, cx + 45, cy + 45], fill=accent, outline=ink, width=8)
        elif family == "pair":
            for side in (-1, 1):
                x = cx + side * 80
                poly([(x - 42, cy - 190), (x + 42, cy - 190), (x + 30, cy - 10), (x + 52, cy + 150), (x + 75, cy + 195),
                      (x - 60, cy + 195), (x - 34, cy + 150), (x - 30, cy - 10)], main)
                draw.ellipse([x - 30, cy - 40, x + 30, cy + 20], fill=second, outline=ink, width=8)
        elif family == "burst":
            spikes, outer, inner = rng.choice([9, 11, 13]), rng.randint(195, 215), rng.randint(105, 130)
            pts = []
            for k in range(spikes * 2):
                a = math.tau * k / (spikes * 2) + rng.uniform(-0.06, 0.06)
                r = outer * rng.uniform(0.85, 1.0) if k % 2 == 0 else inner
                pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
            poly(pts, second)
            blob(95, 3, 0.15, light)
            draw.ellipse([cx - 40, cy - 40, cx + 40, cy + 40], fill=accent, outline=ink, width=6)
        else:
            blob(175, 4, 0.22, main)
            blob(85, 3, 0.18, second, rng.randint(-30, 30), rng.randint(-30, 30))
            draw.ellipse([cx - 28, cy - 70, cx + 28, cy - 14], fill=accent, outline=ink, width=6)


# --------------------------------------------------------------------------------------------- diffusers

class DiffusersImage(Backend):
    """SDXL or FLUX.1 schnell through Hugging Face diffusers. Weights download on first use (cached by HF)."""

    def __init__(self, name: str):
        self.name = name
        self.model_id = {"sdxl": "stabilityai/stable-diffusion-xl-base-1.0", "flux-schnell": "black-forest-labs/FLUX.1-schnell"}[name]
        self._pipe = None
        self._broken = ""  # set when this GPU is found to produce NaNs; later requests fail fast

    def status(self):
        missing = missing_modules(*AI_MODULES)
        if missing:
            return False, f"AI extras not installed ({', '.join(missing)}): run `uv sync --extra ai`"
        ok, device = torch_device_report()
        if not ok:
            return False, device
        if self._broken:
            return False, self._broken
        note = ""
        if self.name == "flux-schnell":
            import torch

            vram_gb = torch.cuda.get_device_properties(0).total_memory / 2**30
            if vram_gb < 16 or not torch.cuda.is_bf16_supported(including_emulation=False):
                return False, f"{device}: FLUX.1 schnell needs ~24 GB VRAM and native bfloat16 (Ampere or newer); use a rented GPU"
            note = " (gated: accept the license on Hugging Face and `hf auth login`)"
        return True, f"{device}; weights load on first request{note}"

    def _load(self):
        if self._pipe is not None:
            return self._pipe
        import torch

        vram_gb = torch.cuda.get_device_properties(0).total_memory / 2**30
        if self.name == "sdxl":
            from diffusers import AutoencoderKL, StableDiffusionXLPipeline

            vae = AutoencoderKL.from_pretrained("madebyollin/sdxl-vae-fp16-fix", torch_dtype=torch.float16)
            pipe = StableDiffusionXLPipeline.from_pretrained(
                self.model_id, vae=vae, torch_dtype=torch.float16, variant="fp16", use_safetensors=True)
            low_vram = vram_gb < 14
        else:
            from diffusers import FluxPipeline

            pipe = FluxPipeline.from_pretrained(self.model_id, torch_dtype=torch.bfloat16)
            low_vram = vram_gb < 30

        if low_vram:
            pipe.enable_model_cpu_offload()  # keeps only the active sub-model on the GPU
        else:
            pipe.to("cuda")
        pipe.vae.enable_slicing()
        pipe.vae.enable_tiling()
        self._pipe = pipe
        return pipe

    def generate(self, prompt, negative_prompt, seed, count, palette, params):
        import torch

        if self._broken:
            raise RuntimeError(self._broken)
        pipe = self._load()
        size = int(params.get("size", 256))
        render = int(params.get("renderSize", 1024))

        def check_nan(_pipe, step, _timestep, kwargs):
            # Some GPUs (Turing without tensor cores: GTX 16xx, T400-T1200) overflow to NaN in fp16 and decode
            # to solid black. Stop at the first bad step instead of rendering the whole set.
            if torch.isnan(kwargs["latents"]).any():
                device = torch.cuda.get_device_name(0)
                self._broken = (f"{self.name} produces NaN latents in half precision on {device} (step {step}); "
                                "this GPU can't run it. Use a rented GPU (--image-url).")
                raise RuntimeError(self._broken)
            return kwargs

        outputs = []
        for i in range(count):
            generator = torch.Generator("cpu").manual_seed(seed + i)
            nan_check = dict(callback_on_step_end=check_nan, callback_on_step_end_tensor_inputs=["latents"])
            if self.name == "sdxl":
                image = pipe(prompt=prompt, negative_prompt=negative_prompt or None, width=render, height=render,
                             num_inference_steps=int(params.get("steps", 30)),
                             guidance_scale=float(params.get("guidance", 6.5)), generator=generator, **nan_check).images[0]
            else:  # schnell is guidance-distilled: 4 steps, no CFG, no negative prompt
                image = pipe(prompt=prompt, width=render, height=render, num_inference_steps=int(params.get("steps", 4)),
                             guidance_scale=0.0, max_sequence_length=256, generator=generator, **nan_check).images[0]
            data, metrics = finish_icon(image, size)
            metrics["renderSize"] = render
            outputs.append(Output(seed + i, "png", data, metrics))
        return outputs


BACKENDS: dict[str, Backend] = {
    "procedural-image": ProceduralImage(),
    "sdxl": DiffusersImage("sdxl"),
    "flux-schnell": DiffusersImage("flux-schnell"),
}
