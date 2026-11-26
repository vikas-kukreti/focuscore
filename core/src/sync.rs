//! HTTP delta-sync client (POST /sync) using Last-Write-Wins conflict resolution.

use crate::models::{SyncResult, Task, TimeEntry, TimerMode};
use crate::store::Store;
use crate::time_now_ms;
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use std::time::Duration;

const SYNC_PATH: &str = "/sync";

#[derive(Serialize, Deserialize)]
struct SyncRequest {
    last_sync: i64,
    tasks: Vec<TaskPayload>,
    time_entries: Vec<TimeEntryPayload>,
}

#[derive(Serialize, Deserialize)]
struct TaskPayload {
    id: String,
    parent_id: Option<String>,
    title: String,
    notes: String,
    status: String,
    scheduled_date: Option<String>,
    timebox_start_min: Option<i32>,
    timebox_duration_min: Option<i32>,
    reminder_enabled: i32,
    sort_order: i32,
    created_at: i64,
    updated_at: i64,
    deleted_at: Option<i64>,
    is_dirty: i32,
}

#[derive(Serialize, Deserialize)]
struct TimeEntryPayload {
    id: String,
    task_id: String,
    mode: String,
    started_at: i64,
    ended_at: i64,
    duration_sec: i32,
    created_at: i64,
    updated_at: i64,
    deleted_at: Option<i64>,
    is_dirty: i32,
}

#[derive(Deserialize)]
struct SyncResponse {
    tasks: Vec<TaskPayload>,
    time_entries: Vec<TimeEntryPayload>,
}

pub struct SyncClient {
    base_url: String,
    token: String,
    last_sync: std::sync::Mutex<i64>,
    store: std::sync::Mutex<Option<Arc<Store>>>,
}

impl SyncClient {
    pub fn new(base_url: String, token: String) -> Self {
        Self {
            base_url,
            token,
            last_sync: std::sync::Mutex::new(0),
            store: std::sync::Mutex::new(None),
        }
    }

    pub fn set_store(&self, store: Arc<Store>) {
        *self.store.lock().unwrap() = Some(store);
    }

    pub fn set_last_sync(&self, v: i64) {
        *self.last_sync.lock().unwrap() = v;
    }

    pub fn last_sync(&self) -> i64 {
        *self.last_sync.lock().unwrap()
    }

    pub fn sync(&self) -> Result<SyncResult, String> {
        let store = self.store.lock().unwrap().clone();
        let store = store.ok_or("sync store not set")?;

        let last_sync = self.last_sync();

        let dirty_tasks = store.dirty_tasks().map_err(|e| e.to_string())?;
        let dirty_entries = store.dirty_time_entries().map_err(|e| e.to_string())?;

        let req = SyncRequest {
            last_sync,
            tasks: dirty_tasks.iter().map(task_to_payload).collect(),
            time_entries: dirty_entries.iter().map(entry_to_payload).collect(),
        };

        let client = ureq::AgentBuilder::new()
            .timeout(Duration::from_secs(10))
            .build();
        let resp: SyncResponse = client
            .post(&format!("{}{}", self.base_url, SYNC_PATH))
            .set("Authorization", &format!("Bearer {}", self.token))
            .set("Content-Type", "application/json")
            .send_json(&req)
            .map_err(|e| format!("sync request failed: {}", e))?
            .into_json()
            .map_err(|e| format!("sync response parse failed: {}", e))?;

        let mut tasks_pulled = 0i32;
        for t in &resp.tasks {
            let existing = store.get_task(&t.id).map_err(|e| e.to_string())?;
            let should_write =
                existing.is_none() || existing.as_ref().unwrap().updated_at <= t.updated_at;
            if should_write {
                let task = Task {
                    id: t.id.clone(),
                    parent_id: t.parent_id.clone(),
                    title: t.title.clone(),
                    notes: t.notes.clone(),
                    status: t.status.clone(),
                    scheduled_date: t.scheduled_date.clone(),
                    timebox_start_min: t.timebox_start_min,
                    timebox_duration_min: t.timebox_duration_min,
                    reminder_enabled: t.reminder_enabled != 0,
                    sort_order: t.sort_order,
                    created_at: t.created_at,
                    updated_at: t.updated_at,
                    deleted_at: t.deleted_at,
                };
                store.update_task(&task).map_err(|e| e.to_string())?;
                tasks_pulled += 1;
            }
        }

        let mut entries_pulled = 0i32;
        for payload in &resp.time_entries {
            let entry = crate::models::TimeEntry {
                id: payload.id.clone(),
                task_id: payload.task_id.clone(),
                mode: match payload.mode.as_str() {
                    "pomodoro" => TimerMode::Pomodoro,
                    _ => TimerMode::Stopwatch,
                },
                started_at: payload.started_at,
                ended_at: payload.ended_at,
                duration_sec: payload.duration_sec,
                created_at: payload.created_at,
                updated_at: payload.updated_at,
                deleted_at: payload.deleted_at,
            };
            store.insert_time_entry(&entry).map_err(|e| e.to_string())?;
            entries_pulled += 1;
        }

        let pushed_task_ids: Vec<String> = dirty_tasks.iter().map(|t| t.id.clone()).collect();
        let pushed_entry_ids: Vec<String> = dirty_entries.iter().map(|e| e.id.clone()).collect();
        store
            .mark_dirty_cleared("tasks", &pushed_task_ids)
            .map_err(|e| e.to_string())?;
        store
            .mark_dirty_cleared("time_entries", &pushed_entry_ids)
            .map_err(|e| e.to_string())?;

        let now = time_now_ms();
        self.set_last_sync(now);

        Ok(SyncResult {
            tasks_pushed: dirty_tasks.len() as i32,
            tasks_pulled,
            entries_pushed: dirty_entries.len() as i32,
            entries_pulled,
        })
    }
}

fn task_to_payload(t: &Task) -> TaskPayload {
    TaskPayload {
        id: t.id.clone(),
        parent_id: t.parent_id.clone(),
        title: t.title.clone(),
        notes: t.notes.clone(),
        status: t.status.clone(),
        scheduled_date: t.scheduled_date.clone(),
        timebox_start_min: t.timebox_start_min,
        timebox_duration_min: t.timebox_duration_min,
        reminder_enabled: t.reminder_enabled as i32,
        sort_order: t.sort_order,
        created_at: t.created_at,
        updated_at: t.updated_at,
        deleted_at: t.deleted_at,
        is_dirty: 1,
    }
}

fn entry_to_payload(e: &TimeEntry) -> TimeEntryPayload {
    TimeEntryPayload {
        id: e.id.clone(),
        task_id: e.task_id.clone(),
        mode: match e.mode {
            TimerMode::Pomodoro => "pomodoro",
            TimerMode::Stopwatch => "stopwatch",
        }
        .to_string(),
        started_at: e.started_at,
        ended_at: e.ended_at,
        duration_sec: e.duration_sec,
        created_at: e.created_at,
        updated_at: e.updated_at,
        deleted_at: e.deleted_at,
        is_dirty: 1,
    }
}
