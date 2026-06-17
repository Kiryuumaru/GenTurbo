"""Internal worker API — not internet-facing, only accessible via Tailscale."""
import json
import shutil
from pathlib import Path
from fastapi import APIRouter, Request, UploadFile, File, Form, HTTPException

from app.shared.schema import (
    WorkerRegisterRequest, WorkerPollRequest, WorkerPollResponse,
    WorkerHeartbeatRequest, JobPayload,
)
from . import jobs as store
from . import workers as worker_store

router = APIRouter(prefix="/worker", tags=["worker"])
FILES_DIR = Path("/app/files")
FILES_DIR.mkdir(parents=True, exist_ok=True)


# ── POST /worker/register ────────────────────────────────────────────────

@router.post(
    "/register",
    summary="Register a worker",
    description="""\
Called by workers at startup. Announces the worker's ID, display name, and
list of model capabilities (model IDs, types, VRAM requirements, param schemas).

Idempotent — re-registering updates models and resets the heartbeat timer.
""",
    responses={
        200: {"description": "Worker registered or updated."},
        422: {"description": "Validation error — check worker_id and models fields."},
    },
)
async def register_worker(req: WorkerRegisterRequest, request: Request):
    host = request.client.host if request.client else "unknown"
    await worker_store.register_worker(req, host)
    return {"status": "registered", "worker_id": req.worker_id}


# ── POST /worker/poll ────────────────────────────────────────────────────

@router.post(
    "/poll",
    summary="Poll for next job",
    description="""\
Called by workers in a loop. Returns the next pending job matching this
worker's capabilities, or `null` if no work is available.

Stale jobs (ASSIGNED/IN_PROGRESS from dead workers) are automatically
recovered and re-queued during poll.
""",
    responses={
        200: {"description": "Job payload returned, or null if idle."},
        422: {"description": "Validation error — check worker_id."},
    },
)
async def poll(req: WorkerPollRequest):
    models = await worker_store.get_worker_models(req.worker_id)
    if not models:
        return WorkerPollResponse(job=None)
    job = await store.poll_job(req.worker_id, models)
    if job is None:
        return WorkerPollResponse(job=None)
    await store.set_in_progress(job["job_id"], req.worker_id)
    return WorkerPollResponse(job=JobPayload(**job))


# ── POST /worker/complete ────────────────────────────────────────────────

@router.post(
    "/complete",
    summary="Report job completion",
    description="""\
Multipart endpoint for workers to report a completed (or failed) job.

**Success path:** upload the generated file with metadata (type, dimensions,
inference time, seed).

**Error path:** send `error` and `error_type` fields without a file.

The job must be ASSIGNED or IN_PROGRESS and belong to this worker.
""",
    responses={
        200: {"description": "Completion accepted."},
        404: {"description": "Job not found or not assigned to this worker."},
        422: {"description": "Validation error — check required fields."},
    },
)
async def complete(
    worker_id: str = Form(..., description="Worker ID."),
    job_id: str = Form(..., description="Job ID being completed."),
    metadata_json: str = Form(default="{}", description="JSON metadata (inference_time_s, dimensions, seed, etc.)."),
    error: str | None = Form(default=None, description="Error message if job failed."),
    error_type: str | None = Form(default=None, description="Exception class name if job failed."),
    file: UploadFile | None = File(default=None, description="Generated output file (image/video/audio/3D)."),
):
    metadata = json.loads(metadata_json)

    file_type = metadata.get("type")
    url = None
    if file and file.filename:
        # Sanitize filename to prevent path traversal: "../../etc/passwd" → "passwd"
        safe_name = Path(file.filename).name
        filepath = FILES_DIR / safe_name
        with filepath.open("wb") as f:
            shutil.copyfileobj(file.file, f)
        url = f"/files/{safe_name}"

    ok = await store.complete_job(
        job_id=job_id, worker_id=worker_id,
        error=error, error_type=error_type,
        type_=file_type, url=url, metadata=metadata,
        inference_time=metadata.get("inference_time_s"),
    )
    if not ok:
        raise HTTPException(status_code=404, detail="Job not found or not assigned to this worker")
    return {"status": "accepted"}


# ── POST /worker/heartbeat ───────────────────────────────────────────────

@router.post(
    "/heartbeat",
    summary="Worker keepalive",
    description="""\
Workers call this every ~30 seconds. Keeps the worker's status as `online`.
Workers that miss heartbeats for 10+ minutes are automatically marked
`offline`, and their in-flight jobs are re-queued.
""",
    responses={
        200: {"description": "Heartbeat acknowledged."},
        422: {"description": "Validation error — check worker_id."},
    },
)
async def heartbeat(req: WorkerHeartbeatRequest):
    from .workers import _worker_stale_cutoff, _purge_cutoff
    ok = await worker_store.heartbeat_worker(req.worker_id, _worker_stale_cutoff(), _purge_cutoff())
    if not ok:
        return {"status": "unknown_worker"}
    return {"status": "ok"}
