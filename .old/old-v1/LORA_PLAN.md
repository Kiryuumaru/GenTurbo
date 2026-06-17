# LoRA on Z-Image-Turbo — Implementation Plan

## Goal

Allow per-request LoRA adapter loading on top of Z-Image-Turbo, matching
fal.ai's `loras` API. Users specify up to 3 LoRAs per request with individual
`path` + `scale` values. Sources: HuggingFace repo IDs, CivitAI links, local
directories. Every request is explicit — no sticky state.

## API Design (matches fal.ai)

### Param: `loras` (array, max 3)

```json
POST /generate
{
  "model": "z-image-turbo",
  "params": {
    "prompt": "a dragon in cinematic lighting at sunset",
    "num_inference_steps": 4,
    "loras": [
      {"path": "sayakpaul/cinematic-lora",      "scale": 0.8},
      {"path": "https://civitai.com/models/12345","scale": 0.5}
    ]
  }
}
```

| Field | Type | Required | Default | Range | Description |
|-------|------|----------|---------|-------|-------------|
| `path` | string | ✅ | — | — | HuggingFace `owner/repo`, CivitAI URL/ID, or local directory name |
| `scale` | number | No | `1.0` | `0.0`–`4.0` | Weight for this LoRA. `0.0` = disabled. `1.0` = normal. `4.0` = max. |
| `huggingface_api_key` | string | No | `null` | — | HF API key for private/gated repos. Falls back to server `HF_TOKEN` env var. |
| `civitai_api_key` | string | No | `null` | — | CivitAI API key for downloads. Falls back to server `CIVITAI_API_KEY` env var. |

| `loras` value | Behavior |
|---------------|----------|
| `null`, `[]`, or absent | Use base model. Unload all previously fused LoRAs. |
| `[{"path": "cinematic", "scale": 1.0}]` | Single LoRA from local/HF/CivitAI |
| `[{"path": "a", "scale": 0.7}, {"path": "b", "scale": 0.5}]` | Two LoRAs blended — diffusers `set_adapters(["a","b"], [0.7,0.5])` + `fuse_lora()` |
| `[{"path": "a"}, {"path": "b"}, {"path": "c"}]` | Three LoRAs (max) — all at default `scale: 1.0` |
| `> 3` entries | `ValueError("Maximum 3 LoRAs per request")` |

### Explicit every request — no sticky state

Every job stands alone. You always send the full `loras` array you want.
If job A uses `[cinematic]` and job B uses `[anime]`, the adapter
unloads cinematic and loads anime. No implicit carry-over.

### Updated param_schema

```python
param_schema = {
    # ... existing entries ...
    "loras": {
        "type": "array",
        "required": False,
        "default": [],
        "maxItems": 3,
        "items": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "LoRA source: HF repo ID (owner/repo), CivitAI URL/ID, or local directory name"},
                "scale": {"type": "number", "default": 1.0, "minimum": 0.0, "maximum": 4.0, "description": "LoRA strength (0.0 = off, 1.0 = normal, 4.0 = max)"},
            },
            "required": ["path"],
        },
        "description": "Up to 3 LoRA adapters. Each has a path (source) and scale (strength). Omit or set to [] for base model.",
    },
}
```

## Verified Pre-requisites ✅

```python
from diffusers import ZImagePipeline
ZImagePipeline.load_lora_weights(..., adapter_name="...")  ✅
ZImagePipeline.set_adapters(["a","b"], weights=[0.7,0.5])  ✅
ZImagePipeline.fuse_lora(adapter_names=["a","b"])           ✅
ZImagePipeline.unfuse_lora()                                ✅
ZImagePipeline.unload_lora_weights()                        ✅
```

Diffusers supports multi-LoRA natively: load each with a unique `adapter_name`,
call `set_adapters([...], weights=[...])` to activate + blend, then `fuse_lora()`
to merge into the base model for fast inference.

**HF repo IDs work out of the box.** No extra code. The worker already has
`HF_TOKEN` set for model downloads, so private/gated repos work too.

### ⚠️ Z-Image-Turbo multi-LoRA caveat

Loading multiple LoRAs on Z-Image-Turbo causes **overexposure and artifacts**
when strengths add up beyond ~1.0. The adapter normalizes strengths using
the same approach as `ComfyUI-ZImage-LoRA-Merger`: keep total "energy" (sum of
squared scales) within bounds.

## Three LoRA Sources (resolved per `path`)

### 1. HuggingFace repo ID (zero-config)

```
"path": "sayakpaul/z-image-turbo-cinematic-lora"
```

Diffusers downloads and caches automatically. Cached in `/root/.cache/huggingface/`
(already mounted at `./data/models`).

### 2. CivitAI model link

```
"path": "https://civitai.com/models/12345/my-lora"
"path": "civitai:12345"
"path": "civitai:12345?version=67890"
```

Downloads via CivitAI API, caches at `data/loras/civitai/{id}-{version}/`.
Requires `CIVITAI_API_KEY` env var.

### 3. Local directory (manual drop)

```
"path": "cinematic"
```

Looks up `data/loras/z-image-turbo/cinematic/pytorch_lora_weights.safetensors`.
No network. Instant load.

## Resolution Order (per `path`)

1. **CivitAI prefix/URL** → `civitai:` or `civitai.com` → download + cache
2. **Local dir** → `data/loras/z-image-turbo/{path}/` exists?
3. **CivitAI cache** → `data/loras/civitai/{path}/` exists?
4. **HuggingFace Hub** → contains `/` → pass to `load_lora_weights()`
5. **Fail** → `ValueError` with format hints

### Storage layout

```
/app/cl/gen-turbo/data/loras/
├── z-image-turbo/              ← manual drop
│   ├── cinematic/
│   │   └── pytorch_lora_weights.safetensors
│   └── anime-style/
│       └── pytorch_lora_weights.safetensors
├── civitai/                     ← CivitAI auto-cache (rw)
│   ├── 12345-67890/
│   │   └── pytorch_lora_weights.safetensors
│   └── 99999-11111/
│       └── pytorch_lora_weights.safetensors
└── huggingface/                 ← /root/.cache/huggingface (rw, already exists)
```
```

## Changes

### 1. `z_image.py` — adapter

```python
import os
import requests
from pathlib import Path
from urllib.parse import urlparse, parse_qs

LORAS_DIR = Path("/app/data/loras/z-image-turbo")
CIVITAI_CACHE = Path("/app/data/loras/civitai")
CIVITAI_API = "https://civitai.com/api/v1/models"
MAX_LORAS = 3

_current_loras: list[dict] = []  # [{path, scale, adapter_name}, ...]


def _resolve_path(self, path: str) -> str:
    """Resolve a single LoRA path to a loadable source string.

    Returns:
    - Local directory path (str) → load_lora_weights(dir_path)
    - HF repo ID (str) → load_lora_weights("owner/repo")
    Raises ValueError if unresolved.
    """
    # 1. CivitAI URL or ID prefix
    if path.startswith("civitai:") or "civitai.com" in path:
        return self._resolve_civitai(path)

    # 2. Local directory
    local = LORAS_DIR / path / "pytorch_lora_weights.safetensors"
    if local.exists():
        return str(local.parent)

    # 3. CivitAI cache hit
    cached = CIVITAI_CACHE / path / "pytorch_lora_weights.safetensors"
    if cached.exists():
        return str(cached.parent)

    # 4. HuggingFace repo ID (contains '/')
    if "/" in path:
        return path  # pass through → diffusers downloads

    raise ValueError(
        f"LoRA path '{path}' not found. Supported: "
        f"1) local dir under {LORAS_DIR}, "
        f"2) HuggingFace repo ID (e.g. 'owner/repo'), "
        f"3) CivitAI URL or 'civitai:12345'"
    )


def _resolve_civitai(self, identifier: str) -> str:
    """Download CivitAI LoRA to cache, return local directory path."""
    model_id, version_id = self._parse_civitai(identifier)

    cache_dir = CIVITAI_CACHE / f"{model_id}-{version_id or 0}"
    weights = cache_dir / "pytorch_lora_weights.safetensors"
    if weights.exists():
        return str(cache_dir)

    api_key = os.environ.get("CIVITAI_API_KEY", "")
    if not api_key:
        raise ValueError("CIVITAI_API_KEY not set. Set in .env or download manually.")

    headers = {"Authorization": f"Bearer {api_key}"}

    # Get model metadata
    resp = requests.get(f"{CIVITAI_API}/{model_id}", headers=headers, timeout=30)
    resp.raise_for_status()
    model_data = resp.json()

    # Find version download URL
    version = self._find_version(model_data, version_id)
    download_url = version["downloadUrl"]

    # Download
    cache_dir.mkdir(parents=True, exist_ok=True)
    resp = requests.get(download_url, headers=headers, timeout=120)
    resp.raise_for_status()
    weights.write_bytes(resp.content)

    print(f"  Downloaded CivitAI LoRA {model_id} → {cache_dir}")
    return str(cache_dir)


def _apply_loras(self, loras: list[dict]) -> None:
    """Ensure the pipeline has exactly the specified LoRAs loaded + fused.

    Compares the requested loras to _current_loras and applies only the diff.
    If loras is empty, unloads all.
    """
    if len(loras) > MAX_LORAS:
        raise ValueError(f"Maximum {MAX_LORAS} LoRAs per request, got {len(loras)}")

    # Check if the set is identical (no-op)
    current_keys = frozenset((l["path"], l.get("scale", 1.0)) for l in self._current_loras)
    new_keys = frozenset((l["path"], l.get("scale", 1.0)) for l in loras)
    if current_keys == new_keys:
        return  # nothing changed

    # Unload everything
    if self._current_loras:
        self._pipe.unfuse_lora()
        self._pipe.unload_lora_weights()
        self._current_loras = []
        print("  LoRAs unloaded")

    if not loras:
        return  # base model only

    # Load each LoRA with a unique adapter_name
    names = []   # adapter names for set_adapters()
    weights = [] # scale values for set_adapters()
    for i, l in enumerate(loras):
        path = l["path"]
        scale = l.get("scale", 1.0)
        name = f"lora_{i}"  # adapter_name
        source = self._resolve_path(path)
        self._pipe.load_lora_weights(source, adapter_name=name)
        names.append(name)
        weights.append(scale)
        print(f"  LoRA [{name}] {path} loaded (scale={scale})")

    # Activate + fuse
    self._pipe.set_adapters(names, adapter_weights=weights)
    self._pipe.fuse_lora(adapter_names=names)
    print(f"  LoRAs fused: {dict(zip(names, weights))}")


def generate(self, params: dict) -> Path:
    # ... existing prompt/step/width validation ...

    loras = params.get("loras", [])
    if not isinstance(loras, list):
        raise ValueError("loras must be an array of {path, scale} objects")
    self._apply_loras(loras)

    # ... existing generation code ...
```

### 2. `worker-compose.yml` — volume mounts + env

```yaml
volumes:
  - ./data/loras:/app/data/loras:rw       # LoRA adapters (rw for CivitAI cache)
  - ./data/models:/root/.cache/huggingface # HF cache (already exists)

environment:
  - CIVITAI_API_KEY=${CIVITAI_API_KEY:-}   # optional, for CivitAI downloads
```

### 3. `base.py` — unload cleanup

```python
def unload(self):
    if hasattr(self, '_current_loras') and self._current_loras:
        self._pipe.unfuse_lora()
        self._pipe.unload_lora_weights()
        self._current_loras = []
    self._pipe = None
    gc.collect()
    torch.cuda.empty_cache()
```

### 4. `.env` on vulcan — new dirs + env

```bash
mkdir -p /app/cl/gen-turbo/data/loras/z-image-turbo
mkdir -p /app/cl/gen-turbo/data/loras/civitai
echo 'CIVITAI_API_KEY=your_key_here' >> /app/cl/gen-turbo/.env
```

### 5. `README.md` — new LoRA section

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| `loras: []` or absent | Unload all LoRAs, generate with base model |
| `loras: [{"path":"cinematic","scale":1.0}]` | Single LoRA loaded + fused |
| `loras: [{"path":"a"},{"path":"b"}]` | Two LoRAs via `set_adapters(["lora_0","lora_1"],[1.0,1.0])` |
| `loras: [{"path":"a","scale":0.7},{"path":"b","scale":0.5}]` | Two LoRAs with custom blend weights |
| Same `loras` as previous job | No-op — hash comparison, skip reload |
| One LoRA removed from previous set | Full unload + reload remaining |
| `> 3` entries | `ValueError("Maximum 3 LoRAs per request, got N")` |
| `path` not found anywhere | `ValueError` with format hints |
| HF download fails (no network / bad token) | `load_lora_weights` raises — worker catches, reports as error |
| CivitAI download fails (no API key / rate limit) | `requests.HTTPError` → caught, reported |
| `civitai:12345` without `CIVITAI_API_KEY` | `ValueError("CIVITAI_API_KEY not set")` |
| `scale` > 4.0 or < 0.0 | Clamped (Pydantic validation if enforced, otherwise adapter clamps) |
| Worker restart | Pipeline reloads, all LoRAs loaded fresh. HF: from cache. CivitAI: from disk cache. |
| Model switch (future multi-model) | `unload()` cleans up LoRAs + pipeline |
| Corrupted safetensors file | `load_lora_weights` raises — caught, reported |

## Testing Plan

1. **Single LoRA via HF**: `loras: [{"path":"sayakpaul/some-lora"}]` → auto-download + generate
2. **Single LoRA via HF (cache)**: re-submit same → cache hit, no re-download
3. **Single LoRA via CivitAI**: `loras: [{"path":"civitai:12345"}]` → API download + generate
4. **Single LoRA via CivitAI (cache)**: re-submit → disk cache hit
5. **Single LoRA via local**: `loras: [{"path":"cinematic"}]` → local file load
6. **Two LoRAs blended**: `[{"path":"a","scale":0.7},{"path":"b","scale":0.5}]` → both loaded, fused
7. **Three LoRAs**: three entries → all three loaded via `set_adapters()`
8. **Switch sets**: `[cinematic]` → `[anime]` → correctly unloads + reloads
9. **Same set, skip**: `[cinematic]` → `[cinematic]` → no-op (hash match)
10. **Empty → loaded → empty**: `[cinematic]` → `[]` → LoRAs unloaded, base model
11. **> 3 rejected**: `loras` with 4 entries → `ValueError`
12. **Unknown path**: `loras: [{"path":"nonexistent"}]` → `ValueError` with format hints
13. **No API key**: `loras: [{"path":"civitai:12345"}]` without `CIVITAI_API_KEY` → clear error
14. **Worker restart**: after restart, submit previously-used HF lora → loads from HF cache

## Files Changed (5)

| File | Change |
|------|--------|
| `app/worker/adapters/image/z_image.py` | `_apply_loras()`: hash-diff, `adapter_name` + `set_adapters()` + `fuse_lora()`. `_resolve_path()`: 4-source resolution. `_resolve_civitai()`: API download + cache. Updated `generate()`. Updated `param_schema`. |
| `app/worker/adapters/base.py` | `unload()` cleans up LoRA state via `_current_loras` |
| `worker-compose.yml` | `./data/loras:rw` mount, `CIVITAI_API_KEY` env var |
| `.env` (vulcan) | `CIVITAI_API_KEY` (optional), LoRA dirs |
| `README.md` | LoRA section with fal.ai-style API examples |

## Not in Scope

- LoRA training — inference only; users supply pre-trained weights
- > 3 concurrent LoRAs — diffusers limitation + known Z-Image artifact issue
- Strength normalization (ComfyUI-style `normalize`/`sqrt_scale` modes) — v2, stick to raw `scale` for now
- NSFW filter bypass — CivitAI API may require auth for certain models
- CivitAI model format conversion (non-diffusers `.safetensors`) — assume diffusers-compatible weights
- LoRA hotswap — full unload+reload each time; `hotswap=True` can be added later for single-LoRA switching
