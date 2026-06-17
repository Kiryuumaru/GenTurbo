"""SQLite database for gen-turbo orchestrator.
Jobs, workers, and worker-model mappings survive restarts.
"""
import aiosqlite
import json
from datetime import datetime, timezone
from pathlib import Path

DB_PATH = Path("/app/data/gen-turbo.db")

SCHEMA = """
CREATE TABLE IF NOT EXISTS workers (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    host TEXT NOT NULL,
    status TEXT DEFAULT 'online',
    last_heartbeat TEXT,
    registered_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS worker_models (
    worker_id TEXT REFERENCES workers(id) ON DELETE CASCADE,
    model_id TEXT NOT NULL,
    type TEXT NOT NULL,
    vram_required_gb INTEGER,
    param_schema TEXT DEFAULT '{}',
    PRIMARY KEY (worker_id, model_id)
);

CREATE TABLE IF NOT EXISTS jobs (
    id TEXT PRIMARY KEY,
    model TEXT NOT NULL,
    type TEXT NOT NULL,
    status TEXT DEFAULT 'IN_QUEUE',
    params TEXT NOT NULL,
    output TEXT,
    error TEXT,
    error_type TEXT,
    worker_id TEXT,
    created_at TEXT NOT NULL,
    started_at TEXT,
    completed_at TEXT,
    cancelled_at TEXT,
    inference_time REAL,
    priority INTEGER DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status);
CREATE INDEX IF NOT EXISTS idx_jobs_model ON jobs(model);
CREATE INDEX IF NOT EXISTS idx_jobs_type ON jobs(type);
CREATE INDEX IF NOT EXISTS idx_jobs_created ON jobs(created_at);
"""


async def get_db() -> aiosqlite.Connection:
    """Get a database connection with row factory for dict-like access."""
    db = await aiosqlite.connect(str(DB_PATH))
    db.row_factory = aiosqlite.Row  # enables dict-like column access
    await db.execute("PRAGMA journal_mode=WAL")
    await db.execute("PRAGMA foreign_keys=ON")
    return db


async def init_db():
    """Create tables on first run. Apply migrations."""
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    db = await get_db()
    await db.executescript(SCHEMA)

    # Migration: add param_schema column if missing (v2)
    try:
        await db.execute("ALTER TABLE worker_models ADD COLUMN param_schema TEXT DEFAULT '{}'")
    except aiosqlite.OperationalError:
        pass  # column already exists

    # Migration: add cancelled_at column and expiry index (v3)
    try:
        await db.execute("ALTER TABLE jobs ADD COLUMN cancelled_at TEXT")
    except aiosqlite.OperationalError:
        pass  # column already exists
    try:
        await db.execute("CREATE INDEX IF NOT EXISTS idx_jobs_expiry ON jobs(status, completed_at, cancelled_at)")
    except aiosqlite.OperationalError:
        pass  # index already exists

    await db.commit()
    await db.close()


# ---- Helpers ----
def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def _serialize(obj) -> str:
    if hasattr(obj, 'model_dump') and callable(obj.model_dump):
        return json.dumps(obj.model_dump())
    return json.dumps(obj)


def _deserialize(raw: str) -> dict:
    return json.loads(raw)
