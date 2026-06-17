"""gen-turbo orchestrator — FastAPI server.
Deployed on orion (always-on, no GPU needed).
"""
import asyncio
import os
from contextlib import asynccontextmanager

from fastapi import FastAPI

from .db import init_db
from . import jobs as store
from .storage import store as output_store
from .client_routes import router as client_router
from .worker_routes import router as worker_router

RETENTION_HOURS = int(os.environ.get("GEN_TURBO_RETENTION_HOURS", "24"))


async def ttl_sweep_loop():
    """Background task: purge expired jobs + files every 10 minutes."""
    while True:
        await asyncio.sleep(600)
        try:
            expired = await store.purge_expired(RETENTION_HOURS)
            for job in expired:
                output = job.get("output")
                if output:
                    url = output.get("url", "")
                    if url:
                        output_store.delete(url)
            if expired:
                print(f"[ttl] purged {len(expired)} expired job(s)")
        except Exception as e:
            print(f"[ttl] sweep error: {e}")


@asynccontextmanager
async def lifespan(app: FastAPI):
    await init_db()
    await store.init_store()
    # Start TTL sweeper in background
    task = asyncio.create_task(ttl_sweep_loop())
    yield
    task.cancel()
    try:
        await task
    except asyncio.CancelledError:
        pass


app = FastAPI(
    title="gen-turbo",
    version="1.0.0",
    summary="Distributed multi-model media generation platform",
    description="""
gen-turbo is a distributed job queue for generative AI models (images, video, audio, 3D).

## Architecture

- **Orchestrator** (this server) — Job queue, worker registry, file serving, REST API
- **Workers** — GPU nodes that poll for jobs, run inference, and upload results

## Quickstart

1. List available models: `GET /models`
2. Check model params: `GET /models/{model_id}`
3. Submit a job: `POST /generate`
4. Poll for completion: `GET /jobs/{request_id}`
5. Download result: `GET /files/{filename}`

## Authentication

All `/worker/*` endpoints are internal-only (Tailscale). Client endpoints are public.
""",
    lifespan=lifespan,
    contact={"name": "gen-turbo", "url": "https://github.com/gen-turbo/gen-turbo"},
    license_info={"name": "MIT"},
    openapi_tags=[
        {"name": "client", "description": "Public API — submit jobs, list models, download results"},
        {"name": "worker", "description": "Internal API — worker registration, polling, heartbeats (Tailscale only)"},
    ],
)

app.include_router(client_router)
app.include_router(worker_router)
