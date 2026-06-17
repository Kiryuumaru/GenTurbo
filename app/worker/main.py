"""gen-turbo worker — polls orchestrator, runs GPU inference.

Usage:
    WORKER_ID=vulcan-worker WORKER_NAME="vulcan (DGX)" ORCHESTRATOR_URL=http://orion-server.local.net:7860 python -m app.worker.main
"""
import os
import signal
import sys
import time
import traceback
import requests

from app.worker.adapters.image.z_image import ZImageAdapter
from app.worker.adapters.base import MediaAdapter
from app.shared.schema import (
    WorkerRegisterRequest, ModelCapability, MediaType,
    WorkerPollRequest, WorkerHeartbeatRequest,
)

ORCHESTRATOR = os.environ.get("ORCHESTRATOR_URL", "http://localhost:7860")
WORKER_ID = os.environ.get("WORKER_ID", "dev-worker")
WORKER_NAME = os.environ.get("WORKER_NAME", "dev")

# Auto-registered adapters — populated by MediaAdapter.__init_subclass__
REGISTERED_ADAPTERS = {}

_shutdown_requested = False


def _handle_signal(signum, frame):
    global _shutdown_requested
    signame = signal.Signals(signum).name
    print(f"[shutdown] received {signame}, finishing current job...")
    _shutdown_requested = True


signal.signal(signal.SIGTERM, _handle_signal)
signal.signal(signal.SIGINT, _handle_signal)


def register():
    """Announce this worker's capabilities to the orchestrator."""
    models = [
        ModelCapability(
            id=a.model_id, type=MediaType(a.type), vram_required_gb=a.vram_required_gb,
            param_schema=getattr(a, 'param_schema', {}),
        )
        for a in REGISTERED_ADAPTERS.values()
    ]
    resp = requests.post(
        f"{ORCHESTRATOR}/worker/register",
        json=WorkerRegisterRequest(worker_id=WORKER_ID, name=WORKER_NAME, models=models).model_dump(),
        timeout=10,
    )
    resp.raise_for_status()
    print(f"Registered as {WORKER_ID} with {[m.id for m in models]}")


def poll():
    """Ask orchestrator for next job. Returns None on failure."""
    try:
        resp = requests.post(
            f"{ORCHESTRATOR}/worker/poll",
            json=WorkerPollRequest(worker_id=WORKER_ID).model_dump(),
            timeout=10,
        )
        resp.raise_for_status()
        return resp.json().get("job")
    except requests.Timeout:
        pass
    except requests.ConnectionError:
        print(f"[poll] orchestrator unreachable: {ORCHESTRATOR}")
    except requests.HTTPError as e:
        print(f"[poll] HTTP {e.response.status_code if e.response is not None else '?'}")
    except Exception as e:
        print(f"[poll] unexpected: {e}")
    return None


def complete(job_id: str, filepath: str | None = None,
             error: str | None = None, error_type: str | None = None,
             metadata: dict | None = None,
             retries: int = 3):
    """Report completion to orchestrator with retries for transient failures.
    Returns True if the report succeeded, False after all retries exhausted."""
    import json as _json
    import time as _time

    for attempt in range(retries):
        try:
            if error:
                resp = requests.post(
                    f"{ORCHESTRATOR}/worker/complete",
                    data={"worker_id": WORKER_ID, "job_id": job_id, "error": error, "error_type": error_type or "UNKNOWN"},
                    timeout=30,
                )
            else:
                with open(filepath, "rb") as f:
                    resp = requests.post(
                        f"{ORCHESTRATOR}/worker/complete",
                        data={
                            "worker_id": WORKER_ID,
                            "job_id": job_id,
                            "metadata_json": _json.dumps(metadata or {}),
                        },
                        files={"file": (filepath.split("/")[-1], f, "application/octet-stream")},
                        timeout=30,
                    )
            resp.raise_for_status()
            return True
        except FileNotFoundError:
            print(f"[complete] file not found: {filepath}")
            _complete_error_only(job_id, f"output file vanished: {filepath}", "FILE_LOST")
            return False
        except requests.Timeout:
            delay = min(2 ** attempt, 8)
            print(f"[complete] timeout (attempt {attempt + 1}/{retries}), retrying in {delay}s...")
            _time.sleep(delay)
        except requests.ConnectionError:
            delay = min(2 ** attempt, 8)
            print(f"[complete] unreachable (attempt {attempt + 1}/{retries}), retrying in {delay}s...")
            _time.sleep(delay)
        except requests.HTTPError as e:
            status = e.response.status_code if e.response is not None else '?'
            if status in (500, 502, 503, 504) and attempt < retries - 1:
                delay = min(2 ** attempt, 8)
                print(f"[complete] HTTP {status} (attempt {attempt + 1}/{retries}), retrying in {delay}s...")
                _time.sleep(delay)
            else:
                print(f"[complete] HTTP {status} reporting job {job_id[:8]} — not retrying")
                return False
        except Exception as e:
            print(f"[complete] unexpected error reporting job {job_id[:8]}: {e}")
            return False
    return False


def _complete_error_only(job_id: str, error: str, error_type: str):
    """Fallback: report error without file upload."""
    try:
        requests.post(
            f"{ORCHESTRATOR}/worker/complete",
            data={"worker_id": WORKER_ID, "job_id": job_id, "error": error, "error_type": error_type},
            timeout=10,
        )
    except Exception:
        pass


def heartbeat():
    """Keepalive."""
    try:
        requests.post(
            f"{ORCHESTRATOR}/worker/heartbeat",
            json=WorkerHeartbeatRequest(worker_id=WORKER_ID).model_dump(),
            timeout=5,
        )
    except Exception:
        pass


def main():
    print(f"gen-turbo worker starting: {WORKER_ID} ({WORKER_NAME}) → {ORCHESTRATOR}")
    register()

    last_heartbeat = 0
    current_model: str | None = None
    current_adapter = None

    while True:
        now = time.time()
        if now - last_heartbeat > 30:
            heartbeat()
            last_heartbeat = now

        job = poll()
        if job is None:
            time.sleep(5)
            continue

        # Validate job schema before processing
        if not isinstance(job, dict):
            print(f"[main] poll returned non-dict: {type(job)}")
            time.sleep(10)
            continue
        job_id = job.get("job_id")
        model = job.get("model")
        params = job.get("params")
        if not job_id or not model or params is None:
            print(f"[main] malformed job from orchestrator: {job}")
            time.sleep(10)
            continue

        adapter = REGISTERED_ADAPTERS.get(model)
        if adapter is None:
            complete(job_id, error=f"No adapter for model: {model}", error_type="UNKNOWN_MODEL")
            continue

        try:
            if current_model != model:
                if current_adapter:
                    current_adapter.unload()
                adapter.load()
                current_model = model
                current_adapter = adapter
            elif current_adapter is None or not current_adapter.is_loaded:
                adapter.load()
                current_adapter = adapter

            t0 = time.perf_counter()
            filepath = adapter.generate(params)
            elapsed = time.perf_counter() - t0

            metadata = adapter.get_output_metadata(params, filepath)
            metadata["inference_time_s"] = round(elapsed, 2)

            complete(
                job_id,
                filepath=str(filepath),
                metadata=metadata,
            )
            print(f"Job {job_id[:8]} complete in {elapsed:.1f}s — {filepath}")

        except Exception as e:
            traceback.print_exc()
            complete(job_id, error=str(e), error_type=type(e).__name__)
        finally:
            # If we're shutting down (SIGTERM), report the job as interrupted
            # so the orchestrator doesn't wait 5 minutes for stale recovery.
            if job_id and (current_model != model or current_adapter is None):
                pass  # job already completed above
            elif job_id and current_model == model and current_adapter:
                # Signal handler set by _shutdown_requested
                if _shutdown_requested:
                    print(f"[shutdown] reporting job {job_id[:8]} as interrupted")
                    _complete_error_only(job_id, "worker shutdown mid-job", "WORKER_SHUTDOWN")


if __name__ == "__main__":
    main()
