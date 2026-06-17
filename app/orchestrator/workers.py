"""Worker registry backed by SQLite."""
from datetime import datetime, timedelta, timezone
from .db import get_db, _now, _serialize, _deserialize
from app.shared.schema import WorkerRegisterRequest

_STALE_JOB_TIMEOUT_MINUTES = 5
_STALE_WORKER_TIMEOUT_MINUTES = 10
_OFFLINE_WORKER_PURGE_DAYS = 7


def _stale_cutoff() -> str:
    """ISO timestamp for stale cutoff (now - STALE_JOB_TIMEOUT)."""
    return (datetime.now(timezone.utc) - timedelta(minutes=_STALE_JOB_TIMEOUT_MINUTES)).isoformat()


def _worker_stale_cutoff() -> str:
    """ISO timestamp for stale worker cutoff."""
    return (datetime.now(timezone.utc) - timedelta(minutes=_STALE_WORKER_TIMEOUT_MINUTES)).isoformat()


def _purge_cutoff() -> str:
    """ISO timestamp for purging offline workers (7 days no heartbeat)."""
    return (datetime.now(timezone.utc) - timedelta(days=_OFFLINE_WORKER_PURGE_DAYS)).isoformat()


async def register_worker(req: WorkerRegisterRequest, host: str):
    now = _now()
    db = await get_db()
    await db.execute(
        "INSERT OR REPLACE INTO workers (id, name, host, status, registered_at, last_heartbeat) VALUES (?, ?, ?, 'online', ?, ?)",
        (req.worker_id, req.name, host, now, now),
    )
    await db.execute("DELETE FROM worker_models WHERE worker_id = ?", (req.worker_id,))
    for m in req.models:
        await db.execute(
            "INSERT INTO worker_models (worker_id, model_id, type, vram_required_gb, param_schema) VALUES (?, ?, ?, ?, ?)",
            (req.worker_id, m.id, m.type.value, m.vram_required_gb, _serialize(m.param_schema)),
        )
    await db.commit()
    await db.close()


async def heartbeat_worker(worker_id: str, stale_cutoff: str, purge_cutoff: str) -> bool:
    now = _now()
    db = await get_db()

    # Update this worker's heartbeat and set online
    cursor = await db.execute(
        "UPDATE workers SET status = 'online', last_heartbeat = ? WHERE id = ?",
        (now, worker_id),
    )

    # Mark stale workers as offline
    await db.execute(
        "UPDATE workers SET status = 'offline' WHERE last_heartbeat < ? AND status = 'online'",
        (stale_cutoff,),
    )
    # Purge old offline workers (no heartbeat for > 7 days)
    await db.execute(
        "DELETE FROM workers WHERE status = 'offline' AND last_heartbeat < ?",
        (purge_cutoff,),
    )
    await db.commit()
    affected = cursor.rowcount
    await db.close()
    return affected > 0


async def get_worker_models(worker_id: str) -> list[str]:
    db = await get_db()
    rows = await db.execute_fetchall(
        "SELECT model_id FROM worker_models WHERE worker_id = ?", (worker_id,)
    )
    await db.close()
    return [r["model_id"] for r in rows]


async def get_all_workers() -> list[dict]:
    db = await get_db()
    rows = await db.execute_fetchall("SELECT * FROM workers ORDER BY name")
    workers = []
    for row in rows:
        w = dict(row)
        model_rows = await db.execute_fetchall(
            "SELECT model_id, type, vram_required_gb, param_schema FROM worker_models WHERE worker_id = ?",
            (w["id"],),
        )
        models = [{"id": m["model_id"], "type": m["type"], "vram_required_gb": m["vram_required_gb"], "param_schema": _deserialize(m["param_schema"] or "{}")} for m in model_rows]
        workers.append({
            "id": w["id"], "name": w["name"], "status": w["status"],
            "models": models, "last_heartbeat": w["last_heartbeat"],
        })
    await db.close()
    return workers


async def get_all_models() -> list[dict]:
    """List all unique models across all workers, with most complete param_schema."""
    db = await get_db()
    rows = await db.execute_fetchall(
        "SELECT model_id, type, param_schema FROM worker_models ORDER BY type, model_id, LENGTH(param_schema) DESC"
    )
    await db.close()
    seen = set()
    models = []
    for r in rows:
        if r["model_id"] not in seen:
            seen.add(r["model_id"])
            models.append({"id": r["model_id"], "type": r["type"], "param_schema": _deserialize(r["param_schema"] or "{}")})
    return models
