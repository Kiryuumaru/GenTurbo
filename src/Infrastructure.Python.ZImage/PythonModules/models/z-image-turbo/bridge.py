"""CSnakes bridge for Z-Image-Turbo — called from C# via the C-API.

This module lives in the model's own venv. The C# adapter imports this
module by name and calls its functions directly.
"""
import sys
from pathlib import Path

_SHARED = Path(__file__).resolve().parent.parent / "shared"
_MODEL_DIR = Path(__file__).resolve().parent
if str(_SHARED) not in sys.path:
    sys.path.insert(0, str(_SHARED))
if str(_MODEL_DIR) not in sys.path:
    sys.path.insert(0, str(_MODEL_DIR))

from base import MediaAdapter
from model import ZImageAdapter


def generate_image(
    prompt: str,
    num_inference_steps: int = 9,
    guidance_scale: float = 0.0,
    width: int = 1024,
    height: int = 1024,
    seed: int = -1,
    loras_json: str = "[]",
) -> dict:
    """Run Z-Image-Turbo inference. Returns dict with filepath, seed, width, height."""
    import json

    adapter = MediaAdapter._registry.get("z-image-turbo")
    if adapter is None:
        raise ValueError("Z-Image-Turbo adapter not registered")

    if not adapter.is_loaded:
        adapter.load()

    loras = json.loads(loras_json) if loras_json else []
    params = {
        "prompt": prompt,
        "num_inference_steps": num_inference_steps,
        "guidance_scale": guidance_scale,
        "width": width,
        "height": height,
        "seed": seed,
        "loras": loras,
    }

    metadata = adapter.get_output_metadata(params, "")
    filepath = adapter.generate(params)

    return {
        "filepath": str(filepath),
        "seed": metadata.get("seed", seed),
        "width": metadata.get("width", width),
        "height": metadata.get("height", height),
    }
