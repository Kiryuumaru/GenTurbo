"""Shared LoRA resolution utilities — used by all model adapters."""
import hashlib
import os
import certifi
import requests
from pathlib import Path
from urllib.parse import urlparse, parse_qs


LORAS_DIR = Path("/app/data/loras/z-image-turbo")
CIVITAI_CACHE = Path("/app/data/loras/civitai")
DL_CACHE = Path("/app/data/loras/downloaded")
MAX_LORAS = 3

# External session for public sites — must bypass the container's
# SSL_CERT_FILE / REQUESTS_CA_BUNDLE env vars which only contain
# the internal CA cert. trust_env=False prevents requests from
# inheriting those env vars; verify=certifi gives us the real CAs.
_external_session = requests.Session()
_external_session.trust_env = False
_external_session.verify = certifi.where()


def parse_civitai(identifier: str) -> tuple[int, int | None, str | None]:
    """Parse a CivitAI identifier into (model_id, version_id, host).

    Handles: civitai:12345, civitai:12345?v=67890,
             https://civitai.com/models/12345?modelVersionId=67890,
             https://civitai.red/models/12345/slug?modelVersionId=67890
    Returns host for later API calls (None = default civitai.com)."""
    clean = identifier
    if clean.startswith("civitai:"):
        clean = clean[len("civitai:"):]

    if clean.startswith("http://") or clean.startswith("https://"):
        parsed = urlparse(clean)
        host = f"{parsed.scheme}://{parsed.netloc}"
        parts = parsed.path.strip("/").split("/")
        try:
            model_idx = parts.index("models")
            model_id = int(parts[model_idx + 1])
        except (ValueError, IndexError):
            raise ValueError(f"Could not parse CivitAI URL: {identifier}")
        qs = parse_qs(parsed.query)
        version_id = None
        if "modelVersionId" in qs:
            version_id = int(qs["modelVersionId"][0])
        elif "version" in qs:
            version_id = int(qs["version"][0])
        return model_id, version_id, host

    if "?" in clean:
        base, qs_str = clean.split("?", 1)
        model_id = int(base)
        qs = parse_qs(qs_str)
        version_id = int(qs.get("version", qs.get("modelVersionId", [None]))[0] or 0) or None
        return model_id, version_id, None

    return int(clean), None, None


def is_civitai_model_page(url: str) -> bool:
    """Check if URL is a civitai model page (needs API lookup), not a download link.
    Model pages: /models/12345 — any civitai host (com, red, etc).
    Download links: /api/download/... — raw file."""
    return ("civitai" in url) and "/models/" in url and "/api/" not in url


def download_url(url: str) -> str:
    """Download a raw safetensors file from any URL, cache it, return local path.
    Uses URL hash as cache key so the same URL is only downloaded once."""
    cache_key = hashlib.sha256(url.encode()).hexdigest()[:16]
    cache_dir = DL_CACHE / cache_key
    weights = cache_dir / "pytorch_lora_weights.safetensors"
    if weights.exists():
        return str(cache_dir)

    cache_dir.mkdir(parents=True, exist_ok=True)
    resp = _external_session.get(url, timeout=120, stream=True)
    resp.raise_for_status()
    with open(weights, "wb") as f:
        for chunk in resp.iter_content(chunk_size=8192):
            f.write(chunk)
    print(f"  Downloaded LoRA from {url[:60]} → {cache_dir}")
    return str(cache_dir)


def find_civitai_version(model_data: dict, version_id: int | None) -> dict:
    """Find a specific version or the latest from CivitAI model data."""
    versions = model_data.get("modelVersions", [])
    if not versions:
        raise ValueError("CivitAI model has no versions")
    if version_id is not None:
        for v in versions:
            if v.get("id") == version_id:
                return v
        raise ValueError(f"Version {version_id} not found for this model")
    for v in versions:
        if v.get("downloadUrl"):
            return v
    return versions[0]


def resolve_civitai(
    identifier: str,
    civitai_api_key: str | None = None,
) -> str:
    """Download CivitAI LoRA to cache, return local directory path."""
    model_id, version_id, host = parse_civitai(identifier)

    cache_dir = CIVITAI_CACHE / f"{model_id}-{version_id or 0}"
    weights = cache_dir / "pytorch_lora_weights.safetensors"
    if weights.exists():
        return str(cache_dir)

    key = civitai_api_key or os.environ.get("CIVITAI_API_KEY", "")
    if not key:
        raise ValueError(
            "CIVITAI_API_KEY not set. Pass citivai_api_key in loras param or set in .env."
        )

    api_base = host or "https://civitai.com"
    api_url = f"{api_base}/api/v1/models/{model_id}"

    headers = {"Authorization": f"Bearer {key}"}
    resp = _external_session.get(api_url, headers=headers, timeout=30)
    resp.raise_for_status()
    model_data = resp.json()
    version = find_civitai_version(model_data, version_id)
    download_url_url = version.get("downloadUrl")
    if not download_url_url:
        raise ValueError(f"No download URL found for CivitAI model {model_id}")

    cache_dir.mkdir(parents=True, exist_ok=True)
    resp = _external_session.get(download_url_url, headers=headers, timeout=120)
    resp.raise_for_status()
    weights.write_bytes(resp.content)

    print(f"  Downloaded CivitAI LoRA {model_id} v{version_id or 'latest'} → {cache_dir}")
    return str(cache_dir)


def resolve_lora_source(path: str, civitai_api_key: str | None = None) -> str:
    """Resolve a single LoRA path to a loadable source string.
    Returns local dir path, HF repo ID, or raw download path.
    Raises ValueError if unresolved."""
    # Raw URL
    if path.startswith("http://") or path.startswith("https://"):
        if is_civitai_model_page(path):
            return resolve_civitai(path, civitai_api_key)
        return download_url(path)

    # CivitAI prefix
    if path.startswith("civitai:"):
        return resolve_civitai(path, civitai_api_key)

    # Local directory
    local = LORAS_DIR / path / "pytorch_lora_weights.safetensors"
    if local.exists():
        return str(local.parent)

    # CivitAI cache hit
    cached = CIVITAI_CACHE / path / "pytorch_lora_weights.safetensors"
    if cached.exists():
        return str(cached.parent)

    # HuggingFace repo ID
    if "/" in path:
        return path

    raise ValueError(
        f"LoRA path '{path}' not found. Supported: "
        f"1) local dir under {LORAS_DIR}, "
        f"2) HuggingFace repo ID (e.g. 'owner/repo'), "
        f"3) CivitAI URL or 'civitai:12345'"
    )
