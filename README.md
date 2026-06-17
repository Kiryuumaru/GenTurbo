# gen-turbo

Distributed multi-model media generation platform — orchestrator job queue with GPU workers across the tailnet.

## Servers

| Component | Server | Role |
|-----------|--------|------|
| **Orchestrator** | orion | Job queue, worker registry, REST API, file serving |
| **Worker** | vulcan (DGX) | GPU inference — Z-Image-Turbo on GB10 (128GB shared) |
| **Worker** | canopus (RTX) | GPU inference — Z-Image-Turbo on RTX 4060 Ti (offline pending driver update) |

- **URL**: `https://gen-turbo.local.net`
- **Image (orchestrator)**: `python:3.12-slim` + custom entrypoint
- **Image (worker)**: `nvcr.io/nvidia/pytorch:25.10-py3` (NGC, CUDA 13.0 + PyTorch 2.9.0)
- **Stack**: gen-turbo-tailscale, gen-turbo-nginx, gen-turbo-orchestrator (orion) + gen-turbo-worker (vulcan/canopus)

## Architecture

```
Client → https://gen-turbo.local.net (orion)
           ├── Tailscale sidecar
           ├── Nginx (TLS termination, wildcard certs)
           └── FastAPI orchestrator (port 7860)
                  ├── SQLite (jobs, workers, models)
                  └── /app/files/ (generated output)

Worker loop (vulcan/canopus):
  register() → while: heartbeat(30s) → poll() → load() → generate() → upload complete()
```

Workers poll the orchestrator, run inference on their GPU, and upload results via multipart. The orchestrator is stateless beyond SQLite — restart safely.

## Models

| Model | Worker | VRAM | Type |
|-------|--------|------|------|
| `z-image-turbo` (Tongyi-MAI/Z-Image-Turbo, 6B) | vulcan (DGX) | 14 GB | image |

Add new adapters in `app/worker/adapters/` — they auto-register via `param_schema`.

## API

Documentation at `https://gen-turbo.local.net` (Scalar UI). Key endpoints:

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/generate` | Submit a generation job |
| `GET`  | `/jobs` | List jobs (filterable by status/model/type) |
| `GET`  | `/jobs/{id}` | Job status + output URL when complete |
| `PUT`  | `/jobs/{id}/cancel` | Cancel a queued/running job |
| `DELETE` | `/jobs/{id}` | Permanently delete job + file (COMPLETED/CANCELLED) |
| `DELETE` | `/jobs/{id}?force=true` | Cancel-then-delete — any status |
| `GET`  | `/models` | Available models with param schemas |
| `GET`  | `/models/{id}` | Model details + full param_schema |
| `GET`  | `/files/{filename}` | Download generated file |
| `GET`  | `/health` | Orchestrator health + worker status |

### Status lifecycle

```
IN_QUEUE → ASSIGNED → IN_PROGRESS → COMPLETED
    │                                    │
    └────────── CANCELLED ←──────────────┘ (PUT /cancel)
```

## Configuration

### Orchestrator (orion)

| Env Var | Default | Description |
|---------|---------|-------------|
| `GEN_TURBO_RETENTION_HOURS` | `24` | Auto-delete expired jobs+files after N hours |

### Worker (vulcan/canopus)

| Env Var | Default | Description |
|---------|---------|-------------|
| `WORKER_ID` | `dev-worker` | Unique worker identifier |
| `WORKER_NAME` | `dev` | Human-readable display name |
| `ORCHESTRATOR_URL` | `https://gen-turbo.local.net` | Orchestrator URL |
| `HF_TOKEN` | — | HuggingFace API token for model downloads |

Worker also needs `.env` file at `/app/cl/gen-turbo/.env` with `WORKER_ID` and `WORKER_NAME`.

## Data

- `./data/` — SQLite database (`gen-turbo.db`), Tailscale state
- `./files/` — Generated output files (served via `/files/{filename}`)
- `./cert.pem` + `./cert.key` — TLS certs (from `Personal/certs/`)
- Worker: `./data/models/` — HuggingFace model cache, `./output/` — temp generation output

## Deploy

### Orchestrator (orion)
```bash
cd /app/cl/gen-turbo
docker compose -f orchestration-compose.yml down
docker compose -f orchestration-compose.yml up -d
```

### Worker (vulcan)
```bash
cd /app/cl/gen-turbo
echo 'WORKER_ID=vulcan-worker
WORKER_NAME=vulcan (DGX)
ORCHESTRATOR_URL=https://gen-turbo.local.net' > .env
docker compose -f worker-compose.yml down
docker compose -f worker-compose.yml up -d
```

### Worker (canopus) — same as vulcan, with appropriate worker IDs.

## Healthchecks

- **Orchestrator**: `GET /health` returns `{"status": "ok"}` + worker list
- **Worker**: heartbeat every 30s to orchestrator `/worker/heartbeat`; stale workers (>10 min) marked offline
- **TTL sweep**: background task purges expired jobs every 10 min

## Known Issues

1. **Canopus offline** — RTX 4060 Ti driver 560 too old for NGC PyTorch 25.10 (needs 570+). Pending driver update.
2. **No worker registration retry** — if orchestrator is unreachable at worker startup, the worker process exits. Restart loop recovers.
3. **Force-delete race** — force-deleting an IN_PROGRESS job may leave the generated file as an orphan on the worker's `/app/output/` (orchestrator file is cleaned up). Manual cleanup only.
