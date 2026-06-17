# gen-turbo

Distributed multi-model media generation platform — orchestrator job queue with GPU workers.

## Projects

| Project | Role |
|---------|------|
| `src/Presentation.Api` | **Orchestrator** — REST API, job queue, worker registry, file serving (port 7860) |
| `src/Presentation.Cli` | **Worker CLI** — GPU inference node, polls orchestrator, uploads results |
| `src/Domain` | Domain entities, value objects, repository/UoW interfaces |
| `src/Application` | Service layer, inbound/outbound ports, background workers |
| `src/Infrastructure.Sqlite` | SQLite persistence (jobs, workers, models) |
| `src/Infrastructure.Python` | CSnakes + Python inference bridge (subprocess) |
| `src/Infrastructure.FileSystem` | File output storage |
| `src/Presentation` | Shared CLI base command |

## Architecture

```
Client → Orchestrator (ASP.NET Minimal API, port 7860)
           ├── SQLite (jobs, workers, models)
           ├── /app/files/ (generated output)
           └── Scalar API docs at /

Worker loop:
  register() → while: heartbeat(30s) → poll() → generate() → upload result
```

The orchestrator (C#) manages the job queue. Workers (C# CLI) poll for jobs,
call into Python for GPU inference via CSnakes, and upload results.

## Models

| Model | VRAM | Type |
|-------|------|------|
| `z-image-turbo` (Tongyi-MAI/Z-Image-Turbo, 6B) | 14 GB | image |

Add new adapters in `src/Infrastructure.Python/PythonModules/app/worker/adapters/`.

## API

Documentation at `/` (Scalar UI) and `/openapi.json`. Key endpoints:

| Method | Path | Description |
|--------|------|-------------|
| `GET`  | `/` | Scalar API reference |
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
| `POST` | `/worker/register` | Register a worker (internal) |
| `POST` | `/worker/poll` | Poll for next job (internal) |
| `POST` | `/worker/complete` | Report job completion (internal, multipart) |
| `POST` | `/worker/heartbeat` | Worker keepalive (internal) |

## Run

### Orchestrator

```bash
dotnet run --project src/Presentation.Api
```

Listens on `http://0.0.0.0:7860`.

### Worker

```bash
dotnet run --project src/Presentation.Cli worker \
  --orchestrator-url http://orchestrator-host:7860 \
  --worker-id gpu-01 \
  --worker-name "DGX GPU Node" \
  --model z-image-turbo \
  --vram-per-model 14
```

The worker:
1. Registers with the orchestrator (idempotent)
2. Polls every 5s for jobs
3. Runs inference via CSnakes → Python (app/worker/adapters/)
4. Uploads result files via multipart
5. Heartbeats every 30s

## Build

```bash
dotnet build
```

## Configuration

### Orchestrator

| Env Var | Default | Description |
|---------|---------|-------------|
| `GEN_TURBO_RETENTION_HOURS` | `24` | Auto-delete expired jobs+files after N hours |

### Worker

| Argument | Env Var | Default | Description |
|----------|---------|---------|-------------|
| `--orchestrator-url` | `GEN_TURBO_ORCHESTRATOR_URL` | `http://localhost:7860` | Orchestrator URL |
| `--worker-id` | `WORKER_ID` | `dev-worker` | Unique worker identifier |
| `--worker-name` | `WORKER_NAME` | `dev` | Human-readable name |
| `--model` | `WORKER_MODELS` | `z-image-turbo` | Comma-separated model IDs |
| `--vram-per-model` | `WORKER_VRAM_PER_MODEL` | `14` | VRAM required per model (GB) |
| `-l` / `--log-level` | `LOG_LEVEL` | `Information` | Log verbosity |

## Inference Engine (Python)

The GPU inference code lives in `src/Infrastructure.Python/PythonModules/`. C# calls into it via CSnakes.

```
PythonModules/
├── csnakes_bridge.py        # Module-level functions called by C#
└── app/worker/
    ├── lora.py              # LoRA resolution (HF, CivitAI, URL, local cache)
    └── adapters/
        ├── base.py          # MediaAdapter base class + auto-registry
        └── image/
            └── z_image.py   # Z-Image-Turbo GPU inference pipeline
```

### Status lifecycle

```
InQueue → Assigned → InProgress → Completed
    │                                   │
    └────────── Cancelled ←─────────────┘
```

## Data

- `data/` — SQLite database (`genturbo.db`)
- `files/` — Generated output files (served via `/files/{filename}`)
- `data/loras/` — LoRA weight cache (HF, CivitAI, raw URL)
- Worker: `./output/` — temp generation output

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
