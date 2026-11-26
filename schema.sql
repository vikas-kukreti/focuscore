-- FocusCore shared SQLite schema (UUIDv7 PKs, Unix epoch ms UTC)
-- Embedded by both the Rust core (`include_str!`) and the Go server.

PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY,
    parent_id TEXT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    notes TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'todo',            -- 'todo' | 'done'
    scheduled_date TEXT NULL,                       -- 'YYYY-MM-DD', NULL = backlog/inbox
    timebox_start_min INTEGER NULL,                 -- minutes from midnight
    timebox_duration_min INTEGER NULL,              -- planned duration in minutes
    reminder_enabled INTEGER NOT NULL DEFAULT 1,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,                    -- epoch ms
    updated_at INTEGER NOT NULL,                    -- epoch ms
    deleted_at INTEGER NULL,                        -- soft delete, epoch ms
    is_dirty INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS idx_tasks_scheduled ON tasks(scheduled_date);
CREATE INDEX IF NOT EXISTS idx_tasks_parent ON tasks(parent_id);
CREATE INDEX IF NOT EXISTS idx_tasks_dirty ON tasks(is_dirty);

CREATE TABLE IF NOT EXISTS time_entries (
    id TEXT PRIMARY KEY,
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    mode TEXT NOT NULL,                             -- 'pomodoro' | 'stopwatch'
    started_at INTEGER NOT NULL,                    -- epoch ms
    ended_at INTEGER NOT NULL,                      -- epoch ms
    duration_sec INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    deleted_at INTEGER NULL,
    is_dirty INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS idx_time_entries_task ON time_entries(task_id);
CREATE INDEX IF NOT EXISTS idx_time_entries_dirty ON time_entries(is_dirty);

CREATE TABLE IF NOT EXISTS sync_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
