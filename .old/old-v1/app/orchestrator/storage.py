"""Output file storage — serves and cleans up generated media files."""
from pathlib import Path


_DEFAULT_DIR = Path("/app/files")


class OutputStore:
    """Simple filesystem-backed output storage.

    Used by the orchestrator to save, serve, and delete generated files.
    Replace with an S3-backed implementation if/when object storage is needed.
    """

    def __init__(self, base_dir: Path | str = _DEFAULT_DIR):
        self._dir = Path(base_dir)
        self._dir.mkdir(parents=True, exist_ok=True)

    def save(self, filename: str, data: bytes) -> str:
        """Write data to disk, return the URL path."""
        path = self._dir / filename
        path.write_bytes(data)
        return f"/files/{filename}"

    def delete(self, url: str) -> bool:
        """Delete a file by its URL path. Returns True if deleted."""
        filename = url.rsplit("/", 1)[-1] if "/" in url else url
        if not filename:
            return False
        path = self._dir / filename
        if path.exists():
            path.unlink()
            return True
        return False

    def exists(self, url: str) -> bool:
        """Check if a file exists given its URL path."""
        filename = url.rsplit("/", 1)[-1] if "/" in url else url
        if not filename:
            return False
        return (self._dir / filename).exists()

    def serve_path(self, url: str) -> Path:
        """Resolve a URL path to a filesystem Path for FileResponse."""
        filename = url.rsplit("/", 1)[-1] if "/" in url else url
        return self._dir / filename

    @property
    def dir(self) -> Path:
        return self._dir


# Singleton instance used by routes and TTL sweeper
store = OutputStore()
