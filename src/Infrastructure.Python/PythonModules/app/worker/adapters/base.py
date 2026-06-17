"""Abstract MediaAdapter — base class for all generative models."""
from pathlib import Path
from typing import Any
import torch


class MediaAdapter:
    """Base class for any generative model adapter.

    Subclasses define model_id, type, pipeline_id, and implement
    load/generate/cleanup/get_output_metadata. The worker calls these
    in sequence:
    load() → generate(params) → get_output_metadata()
    And on model switch or shutdown:
    unload() → cleanup()
    """
    model_id: str                    # "z-image-turbo"
    type: str                        # "image", "video", "audio", "3d"
    pipeline_id: str                 # HuggingFace model ID
    vram_required_gb: int
    torch_dtype: torch.dtype = torch.bfloat16
    pipeline_kwargs: dict[str, Any] = {}

    _pipe: Any = None

    # Auto-registry: all subclasses are automatically registered by model_id
    _registry: dict[str, "MediaAdapter"] = {}

    def __init_subclass__(cls, **kwargs):
        super().__init_subclass__(**kwargs)
        if hasattr(cls, "model_id") and cls.model_id:
            MediaAdapter._registry[cls.model_id] = cls()

    def load(self):
        """Load model into GPU. Called before first generate()."""
        raise NotImplementedError

    def unload(self):
        """Free VRAM and cleanup. Called when switching to a different model."""
        self.cleanup()
        self._pipe = None
        import gc
        gc.collect()
        torch.cuda.empty_cache()

    def cleanup(self):
        """Clean up model-specific state (LoRA, etc.). Override in subclasses."""
        if hasattr(self, '_current_loras') and self._current_loras and self._pipe is not None:
            try:
                self._pipe.unfuse_lora()
                self._pipe.unload_lora_weights()
            except Exception:
                pass  # might already be unloaded
            self._current_loras = []

    def generate(self, params: dict) -> Path:
        """Run inference. Returns path to generated file."""
        raise NotImplementedError

    def get_output_metadata(self, params: dict, filepath: str) -> dict:
        """Return metadata dict for the orchestrator (seed, dimensions, etc.).
        Override in subclasses to add media-type-specific fields."""
        return {"type": self.type}

    @property
    def is_loaded(self) -> bool:
        return self._pipe is not None
