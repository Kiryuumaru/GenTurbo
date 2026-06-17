"""CSnakes bridge — standalone functions that CSnakes.Runtime can call from C#.

Each function here wraps the MediaAdapter registry. CSnakes maps C# interface
methods to these module-level functions by name + signature.
"""
import json
from pathlib import Path
import sys
import os

# Allow imports from the parent app package
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from app.worker.adapters.image.z_image import ZImageAdapter
from app.worker.adapters.base import MediaAdapter


def load_model(model_id: str) -> None:
    """Load a model onto the GPU. Idempotent — skips if already loaded."""
    adapter = MediaAdapter._registry.get(model_id)
    if adapter is None:
        raise ValueError(f"Unknown model: {model_id}")
    adapter.load()


def unload_model(model_id: str) -> None:
    """Free VRAM for a model."""
    adapter = MediaAdapter._registry.get(model_id)
    if adapter is None:
        raise ValueError(f"Unknown model: {model_id}")
    adapter.unload()


def generate_image(
    prompt: str,
    num_inference_steps: int = 9,
    guidance_scale: float = 0.0,
    width: int = 1024,
    height: int = 1024,
    seed: int = -1,
    loras_json: str = "[]",
) -> dict:
    """Run Z-Image-Turbo inference. Returns {"filepath": str, "seed": int, "width": int, "height": int}.

    loras_json is a JSON array of {path, scale} objects, e.g.:
        '[{"path": "sayakpaul/cinematic-lora", "scale": 0.8}]'
    """
    model_id = "z-image-turbo"
    adapter = MediaAdapter._registry.get(model_id)
    if adapter is None:
        raise ValueError(f"Unknown model: {model_id}")

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


def get_available_models() -> list[str]:
    """Return list of registered model IDs."""
    return list(MediaAdapter._registry.keys())


def get_model_info(model_id: str) -> dict:
    """Return model metadata: id, type, vram_required_gb, param_schema."""
    adapter = MediaAdapter._registry.get(model_id)
    if adapter is None:
        raise ValueError(f"Unknown model: {model_id}")
    return {
        "model_id": adapter.model_id,
        "type": adapter.type,
        "vram_required_gb": adapter.vram_required_gb,
        "param_schema": getattr(adapter, "param_schema", {}),
    }
