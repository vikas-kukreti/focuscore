//! SQLite store (WAL mode). All writes set `is_dirty = 1` for sync.

use rusqlite::{params, Connection};
use std::path::Path;
use std::sync::Mutex;

use crate::models::{Task, TimeEntry, TimerMode};
use crate::time_now_ms;

/// Schema is the shared repo-root `schema.sql` — single source of truth.
const SCHEMA: &str = include_str!("../../schema.sql");

pub struct Store {
    conn: Mutex<Connection>,
}

impl Store {
    pub fn open(path: &Path) -> rusqlite::Result<Self> {
        let conn = Connection::open(path)?;
        conn.execute_batch(SCHEMA)?;
        Ok(Self {
            conn: Mutex::new(conn),
        })
    }

    fn conn(&self) -> std::sync::MutexGuard<'_, Connection> {
        self.conn.lock().unwrap()
    }

    /// Insert a task. Returns the task id.
    pub fn insert_task(&self, task: &Task) -> rusqlite::Result<String> {
        self.conn().execute(
            "INSERT INTO tasks (id, parent_id, title, notes, status, scheduled_date, \
             timebox_start_min, timebox_duration_min, reminder_enabled, sort_order, \
             created_at, updated_at, deleted_at, is_dirty)
             VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,NULL,1)",
            params![
                task.id,
                task.parent_id,
                task.title,
                task.notes,
                task.status,
                task.scheduled_date,
                task.timebox_start_min,
                task.timebox_duration_min,
                task.reminder_enabled as i32,
                task.sort_order,
                task.created_at,
                task.updated_at,
            ],
        )?;
        Ok(task.id.clone())
    }

    pub fn list_tasks(&self, scheduled_date: Option<&str>) -> rusqlite::Result<Vec<Task>> {
        let conn = self.conn();
        let (sql, params) = match scheduled_date {
            Some(d) => (
                "SELECT id, parent_id, title, notes, status, scheduled_date, timebox_start_min, \
                 timebox_duration_min, reminder_enabled, sort_order, created_at, updated_at, \
                 deleted_at FROM tasks WHERE scheduled_date = ?1 AND (deleted_at IS NULL) \
                 ORDER BY sort_order, created_at",
                vec![d.to_string()],
            ),
            None => (
                "SELECT id, parent_id, title, notes, status, scheduled_date, timebox_start_min, \
                 timebox_duration_min, reminder_enabled, sort_order, created_at, updated_at, \
                 deleted_at FROM tasks WHERE (deleted_at IS NULL) ORDER BY sort_order, created_at",
                vec![],
            ),
        };
        let mut stmt = conn.prepare(sql)?;
        let rows = stmt.query_map(rusqlite::params_from_iter(params), |r| {
            Ok(Task {
                id: r.get(0)?,
                parent_id: r.get(1)?,
                title: r.get(2)?,
                notes: r.get(3)?,
                status: r.get(4)?,
                scheduled_date: r.get(5)?,
                timebox_start_min: r.get(6)?,
                timebox_duration_min: r.get(7)?,
                reminder_enabled: r.get::<_, i32>(8)? != 0,
                sort_order: r.get(9)?,
                created_at: r.get(10)?,
                updated_at: r.get(11)?,
                deleted_at: r.get(12)?,
            })
        })?;
        let mut out = Vec::new();
        for row in rows {
            out.push(row?);
        }
        Ok(out)
    }

    pub fn get_task(&self, id: &str) -> rusqlite::Result<Option<Task>> {
        let conn = self.conn();
        let result: Result<Task, rusqlite::Error> = conn.query_row(
            "SELECT id, parent_id, title, notes, status, scheduled_date, timebox_start_min, \
             timebox_duration_min, reminder_enabled, sort_order, created_at, updated_at, \
             deleted_at FROM tasks WHERE id = ?1",
            params![id],
            |r| {
                Ok(Task {
                    id: r.get(0)?,
                    parent_id: r.get(1)?,
                    title: r.get(2)?,
                    notes: r.get(3)?,
                    status: r.get(4)?,
                    scheduled_date: r.get(5)?,
                    timebox_start_min: r.get(6)?,
                    timebox_duration_min: r.get(7)?,
                    reminder_enabled: r.get::<_, i32>(8)? != 0,
                    sort_order: r.get(9)?,
                    created_at: r.get(10)?,
                    updated_at: r.get(11)?,
                    deleted_at: r.get(12)?,
                })
            },
        );
        match result {
            Ok(t) => Ok(Some(t)),
            Err(rusqlite::Error::QueryReturnedNoRows) => Ok(None),
            Err(e) => Err(e),
        }
    }

    pub fn update_task(&self, task: &Task) -> rusqlite::Result<usize> {
        let now = time_now_ms();
        self.conn().execute(
            "UPDATE tasks SET title = ?2, notes = ?3, status = ?4, scheduled_date = ?5, \
             timebox_start_min = ?6, timebox_duration_min = ?7, reminder_enabled = ?8, \
             sort_order = ?9, updated_at = ?10, deleted_at = ?11, is_dirty = 1 WHERE id = ?1",
            params![
                task.id,
                task.title,
                task.notes,
                task.status,
                task.scheduled_date,
                task.timebox_start_min,
                task.timebox_duration_min,
                task.reminder_enabled as i32,
                task.sort_order,
                now,
                task.deleted_at,
            ],
        )
    }

    pub fn delete_task(&self, id: &str) -> rusqlite::Result<usize> {
        let now = time_now_ms();
        // Soft delete.
        self.conn().execute(
            "UPDATE tasks SET deleted_at = ?2, is_dirty = 1 WHERE id = ?1",
            params![id, now],
        )
    }

    pub fn insert_time_entry(&self, entry: &TimeEntry) -> rusqlite::Result<String> {
        self.conn().execute(
            "INSERT INTO time_entries (id, task_id, mode, started_at, ended_at, duration_sec, \
             created_at, updated_at, deleted_at, is_dirty)
             VALUES (?1,?2,?3,?4,?5,?6,?7,?8,NULL,1)",
            params![
                entry.id,
                entry.task_id,
                match entry.mode {
                    TimerMode::Pomodoro => "pomodoro",
                    TimerMode::Stopwatch => "stopwatch",
                },
                entry.started_at,
                entry.ended_at,
                entry.duration_sec,
                entry.created_at,
                entry.updated_at,
            ],
        )?;
        Ok(entry.id.clone())
    }

    pub fn list_time_entries_for_task(
        &self,
        task_id: &str,
    ) -> rusqlite::Result<Vec<crate::models::TimeEntry>> {
        let conn = self.conn();
        let mut stmt = conn.prepare(
            "SELECT id, task_id, mode, started_at, ended_at, duration_sec, created_at, updated_at, \
             deleted_at FROM time_entries WHERE task_id = ?1 AND (deleted_at IS NULL) ORDER BY started_at",
        )?;
        let rows = stmt.query_map(params![task_id], |r| {
            Ok(crate::models::TimeEntry {
                id: r.get(0)?,
                task_id: r.get(1)?,
                mode: match r.get::<_, String>(2)?.as_str() {
                    "pomodoro" => crate::models::TimerMode::Pomodoro,
                    _ => crate::models::TimerMode::Stopwatch,
                },
                started_at: r.get(3)?,
                ended_at: r.get(4)?,
                duration_sec: r.get(5)?,
                created_at: r.get(6)?,
                updated_at: r.get(7)?,
                deleted_at: r.get(8)?,
            })
        })?;
        let mut out = Vec::new();
        for row in rows {
            out.push(row?);
        }
        Ok(out)
    }

    /// Dirty task ids for sync (is_dirty = 1), ordered by updated_at.
    /// Dirty tasks for sync (is_dirty = 1), ordered by updated_at.
    pub fn dirty_tasks(&self) -> rusqlite::Result<Vec<Task>> {
        self.list_dirty()
    }

    /// Dirty time entries for sync.
    pub fn dirty_time_entries(&self) -> rusqlite::Result<Vec<crate::models::TimeEntry>> {
        self.list_dirty_time_entries()
    }

    fn list_dirty(&self) -> rusqlite::Result<Vec<Task>> {
        let conn = self.conn();
        let mut stmt = conn.prepare(
            "SELECT id, parent_id, title, notes, status, scheduled_date, timebox_start_min, \
             timebox_duration_min, reminder_enabled, sort_order, created_at, updated_at, \
             deleted_at FROM tasks WHERE is_dirty = 1 ORDER BY updated_at",
        )?;
        let rows = stmt.query_map([], |r| {
            Ok(Task {
                id: r.get(0)?,
                parent_id: r.get(1)?,
                title: r.get(2)?,
                notes: r.get(3)?,
                status: r.get(4)?,
                scheduled_date: r.get(5)?,
                timebox_start_min: r.get(6)?,
                timebox_duration_min: r.get(7)?,
                reminder_enabled: r.get::<_, i32>(8)? != 0,
                sort_order: r.get(9)?,
                created_at: r.get(10)?,
                updated_at: r.get(11)?,
                deleted_at: r.get(12)?,
            })
        })?;
        let mut out = Vec::new();
        for row in rows {
            out.push(row?);
        }
        Ok(out)
    }

    fn list_dirty_time_entries(&self) -> rusqlite::Result<Vec<crate::models::TimeEntry>> {
        let conn = self.conn();
        let mut stmt = conn.prepare(
            "SELECT id, task_id, mode, started_at, ended_at, duration_sec, created_at, updated_at, \
             deleted_at FROM time_entries WHERE is_dirty = 1 ORDER BY updated_at",
        )?;
        let rows = stmt.query_map([], |r| {
            Ok(crate::models::TimeEntry {
                id: r.get(0)?,
                task_id: r.get(1)?,
                mode: match r.get::<_, String>(2)?.as_str() {
                    "pomodoro" => crate::models::TimerMode::Pomodoro,
                    _ => crate::models::TimerMode::Stopwatch,
                },
                started_at: r.get(3)?,
                ended_at: r.get(4)?,
                duration_sec: r.get(5)?,
                created_at: r.get(6)?,
                updated_at: r.get(7)?,
                deleted_at: r.get(8)?,
            })
        })?;
        let mut out = Vec::new();
        for row in rows {
            out.push(row?);
        }
        Ok(out)
    }

    pub fn mark_dirty_cleared(&self, table: &str, ids: &[String]) -> rusqlite::Result<usize> {
        if ids.is_empty() {
            return Ok(0);
        }
        let mut total = 0usize;
        for id in ids {
            total += self.conn().execute(
                &format!("UPDATE {} SET is_dirty = 0 WHERE id = ?1", table),
                params![id],
            )?;
        }
        Ok(total)
    }
}
