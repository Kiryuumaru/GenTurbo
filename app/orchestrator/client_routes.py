"""Client-facing API — fal-compatible job queue for media generation."""
from fastapi import APIRouter, HTTPException, Query
from fastapi.responses import HTMLResponse, FileResponse
from pathlib import Path
from typing import Optional

from app.shared.schema import (
    GenerateRequest, GenerateResponse, JobListResponse, JobStatus,
)
from . import jobs as store
from . import workers as worker_store
from .storage import store as output_store

router = APIRouter(tags=["client"])
FILES_DIR = Path("/app/files")
FILES_DIR.mkdir(parents=True, exist_ok=True)


# ── GET / ────────────────────────────────────────────────────────────────

@router.get(
    "/",
    response_class=HTMLResponse,
    summary="Scalar API Reference",
    description="Interactive API documentation powered by Scalar. All endpoints documented with request/response schemas and examples.",
    include_in_schema=False,
)
def root():
    return """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>gen-turbo API</title>
    <style>body { margin: 0; background: #0d0d0d; }</style>
</head>
<body>
    <script id="api-reference" data-url="/openapi.json"></script>
    <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
</body>
</html>"""


# ── POST /generate ───────────────────────────────────────────────────────

@router.post(
    "/generate",
    response_model=GenerateResponse,
    status_code=201,
    summary="Submit a generation job",
    description="""\
Submit an image, video, audio, or 3D generation job. Returns immediately with a
`request_id` — use `GET /jobs/{request_id}` to poll for completion.

The `params` dict depends on the model. Use `GET /models/{model_id}` to see the
full parameter schema (type, default, min/max, description) for each model.

**Example — z-image-turbo:**

```json
{
  "model": "z-image-turbo",
  "params": {
    "prompt": "a golden retriever puppy in a field of sunflowers",
    "num_inference_steps": 4,
    "guidance_scale": 0.0,
    "width": 1024,
    "height": 1024,
    "seed": -1
  }
}
```

**Typical workflow:**
1. `GET /models` → pick a model
2. `GET /models/{model_id}` → read param schema
3. `POST /generate` → submit job
4. `GET /jobs/{request_id}` → poll until `COMPLETED`
5. `GET /files/{filename}` → download result
""",
    responses={
        201: {"description": "Job accepted — queued for processing."},
        400: {"description": "Unknown model ID."},
        422: {"description": "Validation error — check model and params fields."},
    },
)
async def generate(req: GenerateRequest):
    """Submit a generation job. Returns immediately with a request_id."""
    models = await worker_store.get_all_models()
    model_info = next((m for m in models if m["id"] == req.model), None)
    if model_info is None:
        raise HTTPException(status_code=400, detail=f"Unknown model: {req.model}")
    return await store.create_job(req.model, model_info["type"], req.params)


# ── GET /jobs ────────────────────────────────────────────────────────────

@router.get(
    "/jobs",
    response_model=JobListResponse,
    summary="List jobs",
    description="""\
Returns jobs ordered by creation time (newest first). Supports filtering by
status, model, and media type.

The `count` field is the *total* number of matching jobs (not page size).
Use it for pagination: `?limit=20&offset=40` for page 3.
""",
    responses={
        200: {"description": "Paginated list of jobs."},
        422: {"description": "Invalid query parameter (e.g. limit < 1)."},
    },
)
async def list_jobs(
    status: Optional[JobStatus] = Query(
        None,
        description="Filter by job status (IN_QUEUE, ASSIGNED, IN_PROGRESS, COMPLETED, CANCELLED).",
    ),
    model: Optional[str] = Query(
        None,
        description="Filter by model ID (e.g. 'z-image-turbo').",
    ),
    type: Optional[str] = Query(
        None,
        description="Filter by media type (image, video, audio, 3d).",
    ),
    limit: int = Query(default=20, ge=1, le=100, description="Number of jobs per page (1–100)."),
    offset: int = Query(default=0, ge=0, description="Number of jobs to skip for pagination."),
):
    jobs, total = await store.list_jobs(
        status=status.value if status else None,
        model=model,
        type_=type,
        limit=limit,
        offset=offset,
    )
    return JobListResponse(jobs=jobs, count=total)


# ── GET /jobs/{job_id} ───────────────────────────────────────────────────

@router.get(
    "/jobs/{job_id}",
    summary="Get job status",
    description="""\
Returns full job details: status, timestamps, worker assignment, and output
(if completed). Poll this endpoint to track a job through its lifecycle.

**Lifecycle:** `IN_QUEUE` → `ASSIGNED` → `IN_PROGRESS` → `COMPLETED`

When `COMPLETED`, the response includes `output.url` pointing to the generated
file. If the job failed, `error` and `error_type` are present instead.
""",
    responses={
        200: {"description": "Job found — returns full job object."},
        404: {"description": "Job not found."},
    },
)
async def get_job(job_id: str):
    job = await store.get_job(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Job not found")
    return job


# ── PUT /jobs/{job_id}/cancel ──────────────────────────────────────────

@router.put(
    "/jobs/{job_id}/cancel",
    summary="Cancel a job",
    description="""\
Cancel a queued or in-progress job. The worker will stop processing it on
its next poll.

Works on `IN_QUEUE`, `ASSIGNED`, and `IN_PROGRESS` jobs.
Returns `400` if the job is already `COMPLETED` or `CANCELLED`.
""",
    responses={
        200: {"description": "Job cancelled."},
        400: {"description": "Job already COMPLETED or CANCELLED."},
        404: {"description": "Job not found."},
    },
)
async def cancel_job(job_id: str):
    job = await store.get_job(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Job not found")
    if job["status"] in (JobStatus.COMPLETED.value, JobStatus.CANCELLED.value):
        raise HTTPException(status_code=400, detail=f"Cannot cancel job with status {job['status']}")
    await store.cancel_any_job(job_id)
    return {"request_id": job_id, "status": "CANCELLED"}


# ── DELETE /jobs/{job_id} ───────────────────────────────────────────────

@router.delete(
    "/jobs/{job_id}",
    summary="Permanently delete a job",
    description="""\
Deletes the job record and its generated file from disk. Cannot be undone.

**Without `?force`:** Only deletes `COMPLETED` or `CANCELLED` jobs.
**With `?force=true`:** Cancel-then-delete — works on ALL statuses in one call.
""",
    responses={
        200: {"description": "Job and file deleted."},
        400: {"description": "Job status doesn't allow deletion (use ?force=true to override)."},
        404: {"description": "Job not found."},
    },
)
async def delete_job(
    job_id: str,
    force: bool = Query(
        default=False,
        description="Cancel-then-delete in one shot. Works on ALL statuses.",
    ),
):
    if force:
        deleted = await store.delete_job_forced(job_id)
        if deleted is None:
            raise HTTPException(status_code=404, detail="Job not found")
    else:
        # Check existence FIRST to give the right error
        job = await store.get_job(job_id)
        if job is None:
            raise HTTPException(status_code=404, detail="Job not found")
        if job["status"] not in (JobStatus.COMPLETED.value, JobStatus.CANCELLED.value):
            raise HTTPException(status_code=400, detail=f"Cannot delete job with status {job['status']}. Use ?force=true to cancel+delete in one call.")
        deleted = await store.delete_job(job_id)

    # Remove the generated file
    output = deleted.get("output")
    if output:
        url = output.get("url", "")
        if url:
            output_store.delete(url)
    return {"request_id": job_id, "status": "DELETED"}


# ── GET /models ──────────────────────────────────────────────────────────

@router.get(
    "/models",
    summary="List available models",
    description="""\
Returns all models currently available across connected workers. Each entry
includes the model ID, media type, and a `param_schema` documenting every
accepted parameter (type, default, min/max, description).

Use `GET /models/{model_id}` to get a single model's schema.
""",
    responses={
        200: {"description": "List of available models with param schemas."},
    },
)
async def list_models():
    models = await worker_store.get_all_models()
    return {"models": models, "count": len(models)}


# ── GET /models/{model_id} ───────────────────────────────────────────────

@router.get(
    "/models/{model_id}",
    summary="Get model details",
    description="""\
Returns a single model's metadata including its full `param_schema`.

The schema documents every accepted parameter:
- **type** — data type (string, integer, number)
- **required** — whether the parameter is mandatory
- **default** — fallback value when omitted
- **min** / **max** — valid range (numeric params)
- **description** — human-readable explanation

Use this to construct the `params` dict for `POST /generate`.
""",
    responses={
        200: {"description": "Model found with full param_schema."},
        404: {"description": "Model not found."},
    },
)
async def get_model(model_id: str):
    models = await worker_store.get_all_models()
    m = next((m for m in models if m["id"] == model_id), None)
    if m is None:
        raise HTTPException(status_code=404, detail="Model not found")
    return m


# ── GET /files/{filename} ────────────────────────────────────────────────

@router.get(
    "/files/{filename}",
    summary="Download a generated file",
    description="""\
Serve a generated output file (image PNG, video, audio, 3D model, etc.).

The filename comes from `output.url` in a completed job response.
Example: `/files/abc123.png` → downloads the generated image.
""",
    responses={
        200: {"description": "File served."},
        404: {"description": "File not found."},
    },
)
async def serve_file(filename: str):
    filepath = output_store.serve_path(f"/files/{filename}")
    if not filepath.exists():
        raise HTTPException(status_code=404, detail="File not found")
    return FileResponse(filepath)


# ── GET /health ──────────────────────────────────────────────────────────

@router.get(
    "/health",
    summary="Health check",
    description="""\
Returns orchestrator health and connected worker information.

Each worker entry shows:
- **name** — human-readable label
- **status** — `online` (recent heartbeat) or `offline` (stale)
- **models** — models this worker can serve
""",
    responses={
        200: {"description": "Orchestrator is healthy with worker info."},
    },
)
async def health():
    workers = await worker_store.get_all_workers()
    return {
        "status": "ok",
        "workers": {w["id"]: {"name": w["name"], "status": w["status"], "models": w["models"]} for w in workers},
    }
