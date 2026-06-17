"""Shared Pydantic models for gen-turbo orchestrator <-> worker communication."""
from enum import Enum
from typing import Any, Optional
from pydantic import BaseModel, Field


class JobStatus(str, Enum):
    """Lifecycle of a generation job."""
    IN_QUEUE = "IN_QUEUE"           # Waiting for a worker to pick it up
    ASSIGNED = "ASSIGNED"           # Claimed by a worker, not yet started
    IN_PROGRESS = "IN_PROGRESS"     # Worker is generating
    COMPLETED = "COMPLETED"         # Done — check output.url for the result
    CANCELLED = "CANCELLED"         # Cancelled by user (or force-cancelled)


class MediaType(str, Enum):
    """Output media category."""
    IMAGE = "image"
    VIDEO = "video"
    AUDIO = "audio"
    THREE_D = "3d"


# ---- Client API ---------------------------------------------------------

class GenerateRequest(BaseModel):
    """Request body for POST /generate."""
    model: str = Field(
        description="Model ID (e.g. 'z-image-turbo'). Use GET /models to list available models.",
        json_schema_extra={"example": "z-image-turbo"},
    )
    params: dict[str, Any] = Field(
        description="Model-specific inference parameters. Use GET /models/{model_id} to see the full schema.",
        json_schema_extra={"example": {
            "prompt": "a dragon on a mountain",
            "num_inference_steps": 4,
            "guidance_scale": 0.0,
            "width": 1024,
            "height": 1024,
            "seed": -1,
        }},
    )


class GenerateResponse(BaseModel):
    """Response from POST /generate — job accepted."""
    request_id: str = Field(description="Unique job ID. Use this to poll GET /jobs/{request_id}.")
    status: str = Field(description="Initial job status (always 'IN_QUEUE').")
    model: str = Field(description="Model ID used for this job.")
    created_at: str = Field(description="ISO 8601 timestamp of job creation.")


class JobListResponse(BaseModel):
    """Response from GET /jobs."""
    jobs: list[dict] = Field(description="List of job summaries (most recent first).")
    count: int = Field(description="Total matching jobs (may exceed page size).")


# ---- Worker API ---------------------------------------------------------

class WorkerRegisterRequest(BaseModel):
    """Sent by workers at startup to announce capabilities."""
    worker_id: str = Field(description="Unique worker identifier (e.g. 'vulcan-worker').")
    name: str = Field(description="Human-readable name (e.g. 'vulcan (DGX)').")
    models: list["ModelCapability"] = Field(description="Models this worker can run.")


class ModelCapability(BaseModel):
    """A single model that a worker can serve."""
    id: str = Field(description="Model ID (e.g. 'z-image-turbo').")
    type: MediaType = Field(description="Output media category.")
    vram_required_gb: int = Field(description="Approximate VRAM required, in GB.")
    param_schema: dict[str, Any] = Field(
        default_factory=dict,
        description="Parameter documentation: types, defaults, constraints, descriptions.",
    )


class WorkerPollRequest(BaseModel):
    """Worker asks for the next pending job."""
    worker_id: str = Field(description="Worker ID.")


class WorkerPollResponse(BaseModel):
    """Orchestrator's reply — either a job or null (idle)."""
    job: Optional["JobPayload"] = Field(default=None, description="Next job, or null if no pending work.")


class JobPayload(BaseModel):
    """Job handed to a worker for execution."""
    job_id: str = Field(description="Unique job ID.")
    model: str = Field(description="Model to use for inference.")
    params: dict[str, Any] = Field(description="Inference parameters (model-specific).")


class WorkerCompleteRequest(BaseModel):
    """Worker reports job completion (used internally by worker.py)."""
    worker_id: str = Field(description="Worker ID.")
    job_id: str = Field(description="Job ID being completed.")
    error: str | None = Field(default=None, description="Error message if job failed.")
    error_type: str | None = Field(default=None, description="Exception class name if job failed.")
    type: MediaType | None = Field(default=None, description="Output media type (on success).")
    url: str | None = Field(default=None, description="Relative URL of generated file (on success).")
    metadata: dict[str, Any] | None = Field(default=None, description="Extra metadata (inference time, seed, dimensions, etc.).")


class WorkerHeartbeatRequest(BaseModel):
    """Worker keepalive ping."""
    worker_id: str = Field(description="Worker ID.")
