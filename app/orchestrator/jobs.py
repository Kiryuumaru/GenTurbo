"""Job store backed by SQLite."""
import uuid
from datetime import datetime, timedelta, timezone
from typing import Optional

from .db import get_db, _now, _serialize, _deserialize
from app.shared.schema import JobStatus, MediaType
from .workers import _stale_cutoff, _worker_stale_cutoff, _purge_cutoff


async def init_store():
    """Called at startup after db is initialized."""
    # Reassign any stale ASSIGNED/IN_PROGRESS jobs back to IN_QUEUE
    db = await get_db()
    await db.execute(
        "UPDATE jobs SET status = 'IN_QUEUE', worker_id = NULL, started_at = NULL WHERE status IN ('ASSIGNED', 'IN_PROGRESS')"
    )
    await db.commit()
    await db.close()


# ---- Jobs ----

async def create_job(model: str, type_: str, params: dict) -> dict:
    job_id = str(uuid.uuid4())
    now = _now()
    db = await get_db()
    await db.execute(
        "INSERT INTO jobs (id, model, type, status, params, created_at) VALUES (?, ?, ?, 'IN_QUEUE', ?, ?)",
        (job_id, model, type_, _serialize(params), now),
    )
    await db.commit()
    await db.close()
    return {"request_id": job_id, "status": "IN_QUEUE", "model": model, "created_at": now}


async def poll_job(worker_id: str, capable_models: list[str]) -> Optional[dict]:
    """Return the next pending job this worker can handle, or None.

    Uses atomic UPDATE-then-SELECT to prevent two workers from
    racing on the same job. Also recovers stale jobs from workers
    that haven't heartbeat'd in STALE_JOB_TIMEOUT_MINUTES.
    """
    if not capable_models:
        return None
    placeholders = ",".join("?" for _ in capable_models)
    now = _now()
    cutoff = _stale_cutoff()
    db = await get_db()

    # Recover stale IN_PROGRESS/ASSIGNED jobs from dead workers
    await db.execute(
        """UPDATE jobs SET status = 'IN_QUEUE', worker_id = NULL, started_at = NULL
           WHERE status IN ('ASSIGNED', 'IN_PROGRESS')
           AND worker_id IN (
               SELECT id FROM workers WHERE last_heartbeat < ?
           )""",
        (cutoff,),
    )

    # Also recover jobs assigned to workers no longer online
    await db.execute(
        """UPDATE jobs SET status = 'IN_QUEUE', worker_id = NULL, started_at = NULL
           WHERE status IN ('ASSIGNED', 'IN_PROGRESS')
           AND worker_id NOT IN (SELECT id FROM workers WHERE status = 'online')"""
    )

    # Atomic claim: UPDATE first, then SELECT the claimed row
    # SQLite doesn't support UPDATE ... RETURNING, so we use a
    # subquery in UPDATE, then SELECT the row we just claimed.
    await db.execute(
        f"""UPDATE jobs SET status = 'ASSIGNED', worker_id = ?, started_at = ?
            WHERE id = (
                SELECT id FROM jobs
                WHERE status = 'IN_QUEUE' AND model IN ({placeholders})
                ORDER BY priority DESC, created_at ASC LIMIT 1
            )""",
        (worker_id, now) + tuple(capable_models),
    )
    await db.commit()

    # Read back the job we just claimed
    row = await db.execute_fetchall(
        "SELECT * FROM jobs WHERE worker_id = ? AND status = 'ASSIGNED' AND started_at = ? ORDER BY created_at ASC LIMIT 1",
        (worker_id, now),
    )
    if not row:
        await db.close()
        return None
    job = dict(row[0])
    await db.close()
    return {
        "job_id": job["id"],
        "model": job["model"],
        "params": _deserialize(job["params"]),
    }


async def complete_job(
    job_id: str, worker_id: str,
    error: str | None, error_type: str | None,
    type_: str | None, url: str | None, metadata: dict | None,
    inference_time: float | None,
) -> bool:
    """Mark a job as completed. Only ASSIGNED/IN_PROGRESS jobs can be completed.
    Returns True if the job existed AND was updated."""
    now = _now()
    db = await get_db()
    if error:
        cursor = await db.execute(
            "UPDATE jobs SET status = 'COMPLETED', error = ?, error_type = ?, completed_at = ?, "
            "inference_time = ? WHERE id = ? AND worker_id = ? AND status IN ('ASSIGNED','IN_PROGRESS')",
            (error, error_type, now, inference_time, job_id, worker_id),
        )
    else:
        output = _serialize({"type": type_, "url": url, **(metadata or {})})
        cursor = await db.execute(
            "UPDATE jobs SET status = 'COMPLETED', output = ?, completed_at = ?, inference_time = ? "
            "WHERE id = ? AND worker_id = ? AND status IN ('ASSIGNED','IN_PROGRESS')",
            (output, now, inference_time, job_id, worker_id),
        )
    await db.commit()
    affected = cursor.rowcount
    await db.close()
    return affected > 0


async def set_in_progress(job_id: str, worker_id: str) -> bool:
    """Mark an ASSIGNED job as IN_PROGRESS."""
    db = await get_db()
    cursor = await db.execute(
        "UPDATE jobs SET status = 'IN_PROGRESS' WHERE id = ? AND status = 'ASSIGNED' AND worker_id = ?",
        (job_id, worker_id),
    )
    await db.commit()
    affected = cursor.rowcount
    await db.close()
    return affected > 0


async def cancel_any_job(job_id: str) -> bool:
    """Cancel a job regardless of its current status (IN_QUEUE, ASSIGNED, IN_PROGRESS).
    Returns False if job is already COMPLETED or CANCELLED."""
    now = _now()
    db = await get_db()
    cursor = await db.execute(
        "UPDATE jobs SET status = 'CANCELLED', cancelled_at = ? WHERE id = ? AND status IN ('IN_QUEUE', 'ASSIGNED', 'IN_PROGRESS')",
        (now, job_id),
    )
    await db.commit()
    affected = cursor.rowcount
    await db.close()
    return affected > 0


async def delete_job(job_id: str) -> dict | None:
    """Permanently delete a COMPLETED or CANCELLED job. Returns the job dict
    before deletion (with deserialized output for file cleanup), or None."""
    db = await get_db()
    row = await db.execute_fetchall(
        "SELECT * FROM jobs WHERE id = ? AND status IN ('COMPLETED', 'CANCELLED')",
        (job_id,),
    )
    if not row:
        await db.close()
        return None
    job = dict(row[0])
    await db.execute("DELETE FROM jobs WHERE id = ?", (job_id,))
    await db.commit()
    await db.close()
    # Deserialize JSON fields for the caller
    if isinstance(job.get("output"), str):
        job["output"] = _deserialize(job["output"])
    if isinstance(job.get("params"), str):
        job["params"] = _deserialize(job["params"])
    return job


async def delete_job_forced(job_id: str) -> dict | None:
    """Cancel + delete in one transaction. Works on ALL statuses.
    Resets worker_id so the worker won't try to complete it."""
    now = _now()
    db = await get_db()
    # First read the job (any status)
    row = await db.execute_fetchall("SELECT * FROM jobs WHERE id = ?", (job_id,))
    if not row:
        await db.close()
        return None
    job = dict(row[0])
    # If active, cancel first so worker sees it
    if job["status"] in ("IN_QUEUE", "ASSIGNED", "IN_PROGRESS"):
        await db.execute(
            "UPDATE jobs SET status = 'CANCELLED', cancelled_at = ?, worker_id = NULL WHERE id = ?",
            (now, job_id),
        )
        job["status"] = "CANCELLED"
    # Delete the row
    await db.execute("DELETE FROM jobs WHERE id = ?", (job_id,))
    await db.commit()
    await db.close()
    if isinstance(job.get("output"), str):
        job["output"] = _deserialize(job["output"])
    if isinstance(job.get("params"), str):
        job["params"] = _deserialize(job["params"])
    return job


async def purge_expired(retention_hours: int) -> list[dict]:
    """Delete COMPLETED/CANCELLED jobs older than retention_hours.
    Returns list of deleted jobs (for file cleanup).
    Deletion is atomic (single commit). File cleanup happens after commit —
    if file cleanup crashes, next sweep has no DB rows but files stay orphaned (harmless)."""
    cutoff = (datetime.now(timezone.utc) - timedelta(hours=retention_hours)).isoformat()
    db = await get_db()
    rows = await db.execute_fetchall(
        "SELECT * FROM jobs WHERE status IN ('COMPLETED', 'CANCELLED') AND "
        "(completed_at < ? OR cancelled_at < ?)",
        (cutoff, cutoff),
    )
    deleted = []
    for row in rows:
        job = dict(row)
        deleted.append(job)
        await db.execute("DELETE FROM jobs WHERE id = ?", (job["id"],))
    await db.commit()
    await db.close()
    # Deserialize for file cleanup
    for job in deleted:
        if isinstance(job.get("output"), str):
            job["output"] = _deserialize(job["output"])
        if isinstance(job.get("params"), str):
            job["params"] = _deserialize(job["params"])
    return deleted


async def get_job(job_id: str) -> dict | None:
    db = await get_db()
    row = await db.execute_fetchall("SELECT * FROM jobs WHERE id = ?", (job_id,))
    if not row:
        await db.close()
        return None
    job = dict(row[0])
    await db.close()
    result = {
        "request_id": job["id"],
        "status": job["status"],
        "model": job["model"],
        "type": job["type"],
        "created_at": job["created_at"],
    }
    if job["started_at"]:
        result["started_at"] = job["started_at"]
    if job["completed_at"]:
        result["completed_at"] = job["completed_at"]
    if job["cancelled_at"]:
        result["cancelled_at"] = job["cancelled_at"]
    if job["output"]:
        result["output"] = _deserialize(job["output"])
    if job["error"]:
        result["error"] = job["error"]
        result["error_type"] = job["error_type"]
    if job["inference_time"] is not None:
        result["metrics"] = {"inference_time": job["inference_time"]}
    if job["worker_id"]:
        result["worker_id"] = job["worker_id"]
    return result


async def list_jobs(
    status: str | None = None,
    model: str | None = None,
    type_: str | None = None,
    limit: int = 20,
    offset: int = 0,
) -> tuple[list[dict], int]:
    """Returns (jobs, total_count) for pagination."""
    db = await get_db()
    conditions = []
    params = []
    if status:
        conditions.append("status = ?")
        params.append(status)
    if model:
        conditions.append("model = ?")
        params.append(model)
    if type_:
        conditions.append("type = ?")
        params.append(type_)
    where = f"WHERE {' AND '.join(conditions)}" if conditions else ""

    # Total count (ignores limit/offset) — separate query with same filters
    count_row = await db.execute_fetchall(f"SELECT COUNT(*) as cnt FROM jobs {where}", params)
    total = count_row[0]["cnt"] if count_row else 0

    rows = await db.execute_fetchall(
        f"SELECT * FROM jobs {where} ORDER BY created_at DESC LIMIT ? OFFSET ?",
        params + [limit, offset],
    )
    await db.close()
    jobs = []
    for row in rows:
        j = dict(row)
        item = {"request_id": j["id"], "status": j["status"], "model": j["model"], "type": j["type"], "created_at": j["created_at"]}
        if j["output"]:
            item["error"] = j["error"]
        jobs.append(item)
    return jobs, total
