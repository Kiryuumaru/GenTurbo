"""Z-Image-Turbo adapter — proven on GB10 (aarch64)."""
import sys
from pathlib import Path

_SHARED = Path(__file__).resolve().parent.parent / "shared"
if str(_SHARED) not in sys.path:
    sys.path.insert(0, str(_SHARED))

import os
import uuid

import torch
from diffusers import ZImagePipeline

from base import MediaAdapter
from lora import resolve_lora_source, LORAS_DIR, CIVITAI_CACHE, MAX_LORAS


class ZImageAdapter(MediaAdapter):
    model_id = "z-image-turbo"
    type = "image"
    pipeline_id = "Tongyi-MAI/Z-Image-Turbo"
    vram_required_gb = 14
    torch_dtype = torch.bfloat16
    pipeline_kwargs = {"low_cpu_mem_usage": False}

    param_schema = {
        "prompt": {"type": "string", "required": True, "description": "Text prompt describing the image to generate"},
        "num_inference_steps": {"type": "integer", "default": 9, "min": 1, "max": 50, "description": "Number of denoising steps (more = higher quality, slower)"},
        "guidance_scale": {"type": "number", "default": 0.0, "description": "Classifier-free guidance scale (0.0 = off, 7.5 = strong prompt adherence)"},
        "width": {"type": "integer", "default": 1024, "min": 64, "max": 2048, "description": "Output image width in pixels"},
        "height": {"type": "integer", "default": 1024, "min": 64, "max": 2048, "description": "Output image height in pixels"},
        "seed": {"type": "integer", "default": -1, "description": "Random seed for reproducibility (-1 for random)"},
        "loras": {
            "type": "array", "required": False, "default": [], "maxItems": 3,
            "items": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "LoRA source: HF repo ID, CivitAI URL/ID, raw URL, or local directory"},
                    "scale": {"type": "number", "default": 1.0, "minimum": 0.0, "maximum": 4.0, "description": "LoRA strength (0.0 = off, 1.0 = normal, 4.0 = max)"},
                    "huggingface_api_key": {"type": "string", "description": "Optional HF API key for private/gated repos."},
                    "civitai_api_key": {"type": "string", "description": "Optional CivitAI API key for downloads."},
                },
                "required": ["path"],
            },
            "description": "Up to 3 LoRA adapters. Omit or set to [] for base model.",
        },
    }

    _current_loras: list[dict] = []

    def load(self):
        if self._pipe is not None:
            return
        self._pipe = ZImagePipeline.from_pretrained(
            self.pipeline_id, torch_dtype=self.torch_dtype, **self.pipeline_kwargs
        ).to("cuda")
        self._current_loras = []
        print(f"Z-Image-Turbo loaded on {torch.cuda.get_device_name(0)}")

    def _apply_loras(self, loras: list[dict]) -> None:
        if len(loras) > MAX_LORAS:
            raise ValueError(f"Maximum {MAX_LORAS} LoRAs per request, got {len(loras)}")
        current_keys = frozenset((l["path"], l.get("scale", 1.0)) for l in self._current_loras)
        new_keys = frozenset((l["path"], l.get("scale", 1.0)) for l in loras)
        if current_keys == new_keys:
            return
        if self._current_loras:
            self._pipe.unfuse_lora()
            self._pipe.unload_lora_weights()
            self._current_loras = []
            print("  LoRAs unloaded")
        if not loras:
            return
        names, weights = [], []
        saved_hf_token = os.environ.get("HF_TOKEN", "")
        for i, l in enumerate(loras):
            path = l["path"]
            scale = l.get("scale", 1.0)
            hf_key = l.get("huggingface_api_key")
            civitai_key = l.get("civitai_api_key")
            name = f"lora_{i}"
            if hf_key and "/" in path and "civitai" not in path:
                os.environ["HF_TOKEN"] = hf_key
            elif saved_hf_token:
                os.environ["HF_TOKEN"] = saved_hf_token
            elif "HF_TOKEN" in os.environ:
                del os.environ["HF_TOKEN"]
            source = resolve_lora_source(path, civitai_api_key=civitai_key)
            self._pipe.load_lora_weights(source, adapter_name=name)
            names.append(name)
            weights.append(scale)
            print(f"  LoRA [{name}] {path} loaded (scale={scale})")
        if saved_hf_token:
            os.environ["HF_TOKEN"] = saved_hf_token
        elif "HF_TOKEN" in os.environ and not saved_hf_token:
            del os.environ["HF_TOKEN"]
        self._pipe.set_adapters(names, adapter_weights=weights)
        self._pipe.fuse_lora(adapter_names=names)
        self._current_loras = [{"path": l["path"], "scale": l.get("scale", 1.0)} for l in loras]
        print(f"  LoRAs fused: {dict(zip(names, weights))}")

    def get_output_metadata(self, params: dict, filepath: str) -> dict:
        return {"type": self.type, "seed": params.get("seed", -1), "width": params.get("width", 1024), "height": params.get("height", 1024)}

    def generate(self, params: dict) -> Path:
        prompt = params.get("prompt", "")
        num_inference_steps = params.get("num_inference_steps", 9)
        guidance_scale = params.get("guidance_scale", 0.0)
        width = params.get("width", 1024)
        height = params.get("height", 1024)
        seed = params.get("seed", -1)
        loras = params.get("loras", [])
        if not prompt:
            raise ValueError("prompt is required")
        if num_inference_steps < 1 or num_inference_steps > 50:
            raise ValueError(f"num_inference_steps must be 1-50, got {num_inference_steps}")
        if width < 64 or width > 2048 or height < 64 or height > 2048:
            raise ValueError(f"dimensions must be 64-2048, got {width}x{height}")
        if not isinstance(loras, list):
            raise ValueError("loras must be an array of {path, scale} objects")
        self._apply_loras(loras)
        if seed < 0:
            seed = torch.randint(0, 2**31 - 1, (1,)).item()
        generator = torch.Generator("cuda").manual_seed(seed)
        image = self._pipe(
            prompt=prompt, num_inference_steps=num_inference_steps,
            guidance_scale=guidance_scale, width=width, height=height,
            generator=generator,
        ).images[0]
        filename = f"{uuid.uuid4()}.png"
        filepath = Path("/app/output") / filename
        filepath.parent.mkdir(parents=True, exist_ok=True)
        image.save(filepath)
        return filepath
