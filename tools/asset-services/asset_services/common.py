"""Types shared by the backends."""

from __future__ import annotations

import importlib.util
from dataclasses import dataclass, field


@dataclass
class Output:
    seed: int
    format: str  # "png" | "ogg"
    data: bytes
    metrics: dict = field(default_factory=dict)


class Backend:
    """A generator. `generate` returns finished, post-processed files (one per candidate)."""

    model_id = ""
    placeholder = False

    def status(self) -> tuple[bool, str]:
        return True, "ready"

    def generate(self, prompt: str, negative_prompt: str, seed: int, count: int,
                 palette: list[str], params: dict) -> list[Output]:
        raise NotImplementedError


def missing_modules(*names: str) -> list[str]:
    return [n for n in names if importlib.util.find_spec(n) is None]


def torch_device_report() -> tuple[bool, str]:
    """(cuda available, description) without importing torch twice."""
    import torch

    if not torch.cuda.is_available():
        return False, "CUDA GPU not available (torch reports no device)"
    props = torch.cuda.get_device_properties(0)
    return True, f"{props.name}, {props.total_memory / 2**30:.0f} GB"


def hex_to_rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16)
