"""Audio backends: procedural placeholder synthesis and Stable Audio Open. All share `finish_sfx` post-processing."""

from __future__ import annotations

import io
import random

import numpy as np
import pyloudnorm
import soundfile
from scipy import signal

from .common import Backend, Output, missing_modules, torch_device_report

RATE = 44100
AI_MODULES = ("torch", "diffusers", "transformers", "accelerate", "torchsde")


# --------------------------------------------------------------------------------------------- post-processing

def finish_sfx(audio: np.ndarray, rate: int, target_lufs: float = -16.0, max_peak_db: float = -1.0,
               max_seconds: float | None = None) -> tuple[bytes, dict]:
    """Trim silence, fade, loudness-normalize (ITU-R BS.1770 via pyloudnorm), limit the peak, encode OGG Vorbis.

    `audio` is (samples,) or (samples, channels), float. Returns the file and validation metrics.
    """
    audio = np.asarray(audio, dtype=np.float64)
    if audio.ndim == 1:
        audio = audio[:, None]
    audio = np.nan_to_num(audio)

    # Trim: drop leading/trailing audio quieter than -50 dBFS (keeping 10 ms of lead-in).
    level = np.abs(audio).max(axis=1)
    loud = np.nonzero(level > 10 ** (-50 / 20))[0]
    if loud.size == 0:
        silent = np.zeros((int(0.1 * rate), audio.shape[1]))
        return _ogg(silent, rate), {"durationSeconds": 0.1, "lufs": None, "peakDb": None, "silent": True}
    start = max(0, loud[0] - int(0.01 * rate))
    end = loud[-1] + 1
    if max_seconds:
        end = min(end, start + int(max_seconds * rate))
    audio = audio[start:end]

    # Fades: 5 ms in, 40 ms out (kills clicks at the trim points).
    fade_in, fade_out = min(len(audio), int(0.005 * rate)), min(len(audio), int(0.04 * rate))
    audio[:fade_in] *= np.linspace(0, 1, fade_in)[:, None]
    audio[len(audio) - fade_out:] *= np.linspace(1, 0, fade_out)[:, None]

    # Loudness. BS.1770 gating needs at least one 400 ms block, so short sounds are measured with silence padding
    # (which the gate ignores) — the measured value is the sound's own loudness.
    meter = pyloudnorm.Meter(rate)
    measured = audio if len(audio) >= int(0.4 * rate) else np.vstack([audio, np.zeros((int(0.4 * rate), audio.shape[1]))])
    lufs = meter.integrated_loudness(measured)
    if np.isfinite(lufs):
        audio = audio * 10 ** ((target_lufs - lufs) / 20)

    # Peak limit: a soft (tanh) limiter keeps punchy transients under the ceiling without dropping the whole
    # sound's loudness, then a final hard scale guarantees the ceiling.
    ceiling = 10 ** (max_peak_db / 20)
    if np.abs(audio).max() > ceiling:
        audio = ceiling * np.tanh(audio / ceiling)
        peak = np.abs(audio).max()
        if peak > ceiling:
            audio = audio * (ceiling / peak)

    measured = audio if len(audio) >= int(0.4 * rate) else np.vstack([audio, np.zeros((int(0.4 * rate), audio.shape[1]))])
    final_lufs = meter.integrated_loudness(measured)
    final_peak = np.abs(audio).max()
    metrics = {
        "durationSeconds": round(len(audio) / rate, 3),
        "lufs": round(float(final_lufs), 1) if np.isfinite(final_lufs) else None,
        "peakDb": round(float(20 * np.log10(max(final_peak, 1e-9))), 1),
        "channels": int(audio.shape[1]),
        "sampleRate": rate,
    }
    return _ogg(audio, rate), metrics


def _ogg(audio: np.ndarray, rate: int) -> bytes:
    buffer = io.BytesIO()
    soundfile.write(buffer, audio.astype(np.float32), rate, format="OGG", subtype="VORBIS")
    return buffer.getvalue()


# --------------------------------------------------------------------------------------------- procedural

class ProceduralAudio(Backend):
    """Placeholder SFX: layers chosen from keywords in the prompt (impacts, sweeps, whooshes, clicks, hiss, chimes,
    rumble), with seeded variation. Good enough to hear timing and hook-up in game; replaced by the AI backend."""

    model_id = "procedural-audio@1"
    placeholder = True
    _falling = False  # sweep direction for the current request (requests are served one at a time)

    LAYERS = {
        "impact": ("slam", "boom", "explosion", "crash", "hit", "smash", "thump", "blast", "crunch", "collapsing", "pulse", "whoomp"),
        "metal": ("metal", "clank", "clang", "ring", "bell", "armor", "sword", "ingot", "forging", "clack"),
        "sweep": ("whine", "rising", "hum", "building", "spinning", "power-down", "servo", "gathering", "stalling"),
        "whoosh": ("whoosh", "slash", "swing", "boost", "thruster", "teleport", "suction", "slicing"),
        "clicks": ("burst", "pistol", "rounds", "clicks", "reload", "magazine", "salvo"),
        "hiss": ("hiss", "sizzle", "steam", "spray", "fuse", "gas", "acid", "venting", "sparks"),
        "chime": ("chime", "sparkle", "shimmer", "magic", "zap", "ping", "tones", "crystal", "glassy"),
        "rumble": ("roar", "flame", "fire", "rumble", "deep", "gravity", "flamethrower"),
    }

    def generate(self, prompt, negative_prompt, seed, count, palette, params):
        duration = float(params.get("durationSeconds", 1.5))
        text = prompt.lower()
        layers = [name for name, words in self.LAYERS.items() if any(w in text for w in words)] or ["impact"]
        self._falling = any(w in text for w in ("power-down", "stalling", "falling", "reversed"))
        outputs = []
        for i in range(count):
            rng = random.Random(seed + i)
            nrng = np.random.default_rng(seed + i)
            n = int(duration * RATE)
            t = np.arange(n) / RATE
            mix = np.zeros(n)
            for layer in layers:
                mix += getattr(self, f"_{layer}")(t, n, rng, nrng)
            stereo = np.stack([mix, np.roll(mix, rng.randint(0, 40))], axis=1)  # slight width
            data, metrics = finish_sfx(stereo, RATE, float(params.get("targetLufs", -16)), float(params.get("maxPeakDb", -1)), duration)
            metrics["layers"] = layers
            outputs.append(Output(seed + i, "ogg", data, metrics))
        return outputs

    # Each layer returns a mono float signal of length n.
    @staticmethod
    def _env(t, attack, decay):
        return np.minimum(t / max(attack, 1e-4), 1.0) * np.exp(-t / decay)

    def _impact(self, t, n, rng, nrng):
        noise = signal.lfilter(*signal.butter(2, rng.uniform(800, 2500) / (RATE / 2)), nrng.standard_normal(n))
        thump = np.sin(2 * np.pi * rng.uniform(45, 80) * t * (1 - 0.3 * t)) * self._env(t, 0.002, rng.uniform(0.15, 0.35))
        return 0.7 * noise * self._env(t, 0.001, rng.uniform(0.08, 0.25)) + thump

    def _metal(self, t, n, rng, nrng):
        base = rng.uniform(300, 700)
        partials = [1.0, 2.76, 5.4, 8.93]
        tone = sum(np.sin(2 * np.pi * base * p * t + rng.uniform(0, 6.28)) / (k + 1) for k, p in enumerate(partials))
        return 0.5 * tone * self._env(t, 0.001, rng.uniform(0.2, 0.6))

    def _sweep(self, t, n, rng, nrng):
        rising = not self._falling
        f0, f1 = (rng.uniform(150, 300), rng.uniform(700, 1400)) if rising else (rng.uniform(700, 1200), rng.uniform(80, 160))
        freq = f0 + (f1 - f0) * (t / t[-1]) ** 1.5
        phase = 2 * np.pi * np.cumsum(freq) / RATE
        saw = signal.sawtooth(phase) * 0.3 + np.sin(phase) * 0.5
        vib = 1 + 0.05 * np.sin(2 * np.pi * 6 * t)
        env = np.minimum(t / (t[-1] * 0.8), 1.0) * np.minimum((t[-1] - t) / 0.05, 1.0)
        return 0.45 * saw * vib * env

    def _whoosh(self, t, n, rng, nrng):
        noise = nrng.standard_normal(n)
        center = rng.uniform(600, 1500)
        b, a = signal.butter(2, [center * 0.5 / (RATE / 2), min(center * 2.5, RATE / 2 - 100) / (RATE / 2)], btype="band")
        shaped = signal.lfilter(b, a, noise)
        peak = rng.uniform(0.15, 0.35) * t[-1]
        env = np.exp(-((t - peak) ** 2) / (2 * (0.08 * t[-1] + 0.03) ** 2))
        return 0.9 * shaped * env

    def _clicks(self, t, n, rng, nrng):
        out = np.zeros(n)
        shots = rng.randint(3, 4)
        gap = rng.uniform(0.07, 0.11)
        for k in range(shots):
            start = int((0.01 + k * gap) * RATE)
            length = min(int(0.09 * RATE), n - start)
            if length <= 0:
                break
            tt = np.arange(length) / RATE
            crack = nrng.standard_normal(length) * self._env(tt, 0.0005, 0.02)
            body = np.sin(2 * np.pi * rng.uniform(120, 200) * tt) * self._env(tt, 0.001, 0.04)
            out[start:start + length] += crack + 0.8 * body
        return out

    def _hiss(self, t, n, rng, nrng):
        b, a = signal.butter(2, rng.uniform(3000, 6000) / (RATE / 2), btype="high")
        hiss = signal.lfilter(b, a, nrng.standard_normal(n))
        crackle = (nrng.random(n) > 0.9993) * nrng.standard_normal(n) * 3
        env = np.minimum(t / 0.05, 1.0) * np.minimum((t[-1] - t) / (0.4 * t[-1]), 1.0)
        return 0.35 * (hiss + crackle) * env

    def _chime(self, t, n, rng, nrng):
        out = np.zeros(n)
        base = rng.uniform(600, 1000)
        for k, ratio in enumerate(rng.sample([1.0, 1.25, 1.5, 2.0, 2.5, 3.0], 4)):
            start = int(k * rng.uniform(0.04, 0.09) * RATE)
            tt = t[: n - start]
            tone = np.sin(2 * np.pi * base * ratio * tt) + 0.3 * np.sin(2 * np.pi * base * ratio * 2.01 * tt)
            out[start:] += 0.35 * tone * self._env(tt, 0.002, rng.uniform(0.25, 0.5))
        return out

    def _rumble(self, t, n, rng, nrng):
        b, a = signal.butter(2, rng.uniform(250, 500) / (RATE / 2))
        low = signal.lfilter(b, a, nrng.standard_normal(n))
        trem = 1 + 0.4 * np.sin(2 * np.pi * rng.uniform(7, 14) * t)
        env = np.minimum(t / 0.08, 1.0) * np.minimum((t[-1] - t) / (0.3 * t[-1]), 1.0)
        return 1.2 * low * trem * env


# --------------------------------------------------------------------------------------------- Stable Audio Open

class StableAudioOpen(Backend):
    """Stable Audio Open 1.0 via diffusers. Gated + Stability AI Community License (see assets/models.json)."""

    model_id = "stabilityai/stable-audio-open-1.0"

    def __init__(self):
        self._pipe = None

    def status(self):
        missing = missing_modules(*AI_MODULES)
        if missing:
            return False, f"AI extras not installed ({', '.join(missing)}): run `uv sync --extra ai`"
        ok, device = torch_device_report()
        if not ok:
            return False, device
        return True, f"{device}; gated model: accept the license on Hugging Face and `hf auth login`"

    def _load(self):
        if self._pipe is None:
            import torch
            from diffusers import StableAudioPipeline

            pipe = StableAudioPipeline.from_pretrained(self.model_id, torch_dtype=torch.float16)
            if torch.cuda.get_device_properties(0).total_memory / 2**30 < 12:
                pipe.enable_model_cpu_offload()
            else:
                pipe.to("cuda")
            self._pipe = pipe
        return self._pipe

    def generate(self, prompt, negative_prompt, seed, count, palette, params):
        import torch

        pipe = self._load()
        duration = float(params.get("durationSeconds", 1.5))
        outputs = []
        for i in range(count):
            generator = torch.Generator("cuda").manual_seed(seed + i)
            # Generate a little long; the trim keeps the start of the sound and cuts to the requested length.
            result = pipe(prompt, negative_prompt=negative_prompt or None, num_inference_steps=int(params.get("steps", 100)),
                          guidance_scale=float(params.get("guidance", 7.0)), audio_end_in_s=min(47.0, duration + 1.0),
                          num_waveforms_per_prompt=1, generator=generator)
            audio = result.audios[0].T.float().cpu().numpy()  # (samples, channels)
            rate = pipe.vae.sampling_rate
            data, metrics = finish_sfx(audio, rate, float(params.get("targetLufs", -16)), float(params.get("maxPeakDb", -1)), duration)
            outputs.append(Output(seed + i, "ogg", data, metrics))
        return outputs


BACKENDS: dict[str, Backend] = {
    "procedural-audio": ProceduralAudio(),
    "stable-audio-open": StableAudioOpen(),
}
