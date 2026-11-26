//! FocusCore shared library.
//!
//! Plain-Rust core (store, timer, sync client, models) plus:
//!   - `mod abi`: `#[no_mangle]` C ABI facade consumed by C# (csbindgen).
//!   - `interface.udl`: UniFFI interface definition for Kotlin/Swift bindings
//!     (generated with `uniffi-bindgen`; Rust side implements the same fns).
//!
//! All state lives in a single SQLite database (WAL mode). Time is Unix epoch
//! ms UTC. The timer engine is deterministic (timestamps, not sleep loops).
//!
//! This is an FFI/library-boundary API, so many public functions naturally take
//! several parameters and some items are only used by foreign clients.

#![allow(dead_code)]
#![allow(clippy::too_many_arguments)]

mod abi;
mod models;
mod store;
mod sync;
mod timer;

use std::sync::Arc;
use std::sync::Mutex;

use crate::models::{SyncResult, Task, TimeEntry, TimerMode, TimerState};
use crate::store::Store;
use crate::sync::SyncClient;
use crate::timer::TimerEngine;

static CORE: Mutex<Option<Core>> = Mutex::new(None);

struct Core {
    store: Arc<Store>,
    timer: Mutex<TimerEngine>,
    sync: SyncClient,
}

impl Core {
    fn open_with_path(db_path: Option<&str>) -> Result<Self, String> {
        let path = db_path.unwrap_or("focuscore.db");
        let store = Arc::new(
            Store::open(std::path::Path::new(path)).map_err(|e| format!("open store: {}", e))?,
        );
        Ok(Self {
            store,
            timer: Mutex::new(TimerEngine::new()),
            sync: SyncClient::new(String::new(), String::new()),
        })
    }

    fn open() -> Result<Self, String> {
        Self::open_with_path(None)
    }
}

fn with_core<F, R>(f: F) -> R
where
    F: FnOnce(&Core) -> R,
{
    let mut guard = CORE.lock().unwrap();
    if guard.is_none() {
        *guard = Some(Core::open().expect("failed to open core"));
    }
    f(guard.as_ref().unwrap())
}

fn time_now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

// ----- public free functions (testable; mirrored by UniFFI UDL) -----

pub fn focuscore_version() -> String {
    "focuscore 0.1.0".into()
}

pub fn focuscore_open(db_path: Option<String>) -> Result<String, String> {
    let mut guard = CORE.lock().unwrap();
    if guard.is_some() {
        return Err("core already open".into());
    }
    let core = Core::open_with_path(db_path.as_deref())?;
    *guard = Some(core);
    Ok("ok".into())
}

pub fn focuscore_close() {
    let mut guard = CORE.lock().unwrap();
    *guard = None;
}

pub fn focuscore_quick_add(
    title: String,
    scheduled_date: Option<String>,
) -> Result<String, String> {
    let now = time_now_ms();
    let id = uuid::Uuid::now_v7().to_string();
    let task = crate::models::Task {
        id: id.clone(),
        parent_id: None,
        title,
        notes: String::new(),
        status: "todo".into(),
        scheduled_date,
        timebox_start_min: None,
        timebox_duration_min: None,
        reminder_enabled: true,
        sort_order: 0,
        created_at: now,
        updated_at: now,
        deleted_at: None,
    };
    let db_res = with_core(|core| core.store.insert_task(&task));
    db_res.map(|_| id).map_err(|e| e.to_string())
}

pub fn focuscore_list_tasks(scheduled_date: Option<String>) -> Vec<Task> {
    with_core(|core| {
        core.store
            .list_tasks(scheduled_date.as_deref())
            .unwrap_or_default()
    })
}

pub fn focuscore_get_task(id: String) -> Option<Task> {
    with_core(|core| core.store.get_task(&id).unwrap_or(None))
}

pub fn focuscore_update_task(task: Task) -> Result<(), String> {
    with_core(|core| core.store.update_task(&task).map(|_| ())).map_err(|e| e.to_string())
}

pub fn focuscore_delete_task(id: String) -> Result<(), String> {
    with_core(|core| core.store.delete_task(&id).map(|_| ())).map_err(|e| e.to_string())
}

pub fn focuscore_insert_time_entry(
    id: String,
    task_id: String,
    mode: TimerMode,
    started_at: i64,
    ended_at: i64,
    duration_sec: i32,
    created_at: i64,
    updated_at: i64,
) -> Result<String, String> {
    let entry = crate::models::TimeEntry {
        id: id.clone(),
        task_id,
        mode,
        started_at,
        ended_at,
        duration_sec,
        created_at,
        updated_at,
        deleted_at: None,
    };
    let db_res = with_core(|core| core.store.insert_time_entry(&entry));
    db_res.map(|_| id).map_err(|e| e.to_string())
}

pub fn focuscore_list_time_entries(task_id: String) -> Vec<TimeEntry> {
    with_core(|core| {
        core.store
            .list_time_entries_for_task(&task_id)
            .unwrap_or_default()
    })
}

pub fn focuscore_timer_state() -> TimerState {
    with_core(|core| core.timer.lock().unwrap().state())
}

pub fn focuscore_timer_start(task_id: String, mode: TimerMode, total_sec: i32) {
    with_core(|core| core.timer.lock().unwrap().start(task_id, mode, total_sec));
}

pub fn focuscore_timer_pause() {
    with_core(|core| core.timer.lock().unwrap().pause());
}

pub fn focuscore_timer_resume() {
    with_core(|core| core.timer.lock().unwrap().resume());
}

pub fn focuscore_timer_stop() -> Option<i32> {
    with_core(|core| core.timer.lock().unwrap().stop())
}

pub fn focuscore_timer_reset() {
    with_core(|core| core.timer.lock().unwrap().reset());
}

pub fn focuscore_sync(_base_url: String, _token: String) -> Result<SyncResult, String> {
    with_core(|core| {
        core.sync.set_store(Arc::clone(&core.store));
        core.sync.sync()
    })
    .map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Close any open core and open a fresh temp database for this test.
    fn open_test_db() {
        focuscore_close();
        let nano = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let path = std::env::temp_dir().join(format!("focuscore-test-{}.db", nano));
        focuscore_open(Some(path.to_string_lossy().to_string())).unwrap();
    }

    #[test]
    fn version_nonempty() {
        assert!(!focuscore_version().is_empty());
    }

    #[test]
    fn quick_add_and_list_task() {
        open_test_db();
        let id = focuscore_quick_add("Buy milk".into(), None).unwrap();
        assert!(!id.is_empty());
        let tasks = focuscore_list_tasks(None);
        assert_eq!(tasks.len(), 1);
        assert_eq!(tasks[0].title, "Buy milk");
        assert_eq!(tasks[0].status, "todo");

        // Get + update.
        let got = focuscore_get_task(id.clone()).unwrap();
        assert_eq!(got.title, "Buy milk");
        let mut updated = got;
        updated.title = "Buy milk and eggs".into();
        updated.sort_order = 5;
        focuscore_update_task(updated.clone()).unwrap();
        let got2 = focuscore_get_task(id.clone()).unwrap();
        assert_eq!(got2.title, "Buy milk and eggs");
        assert_eq!(got2.sort_order, 5);

        // Soft delete.
        focuscore_delete_task(id).unwrap();
        let after = focuscore_list_tasks(None);
        assert!(after.is_empty(), "deleted task should not appear");
    }

    #[test]
    fn timer_lifecycle() {
        open_test_db();
        focuscore_timer_start("t1".into(), TimerMode::Pomodoro, 60);
        let st = focuscore_timer_state();
        assert!(matches!(st.kind, crate::models::TimerStateKind::Running));
        assert_eq!(st.remaining_sec, 60);

        focuscore_timer_pause();
        let st = focuscore_timer_state();
        assert!(matches!(st.kind, crate::models::TimerStateKind::Paused));

        focuscore_timer_resume();
        let st = focuscore_timer_state();
        assert!(matches!(st.kind, crate::models::TimerStateKind::Running));

        let done = focuscore_timer_stop();
        assert!(done.is_some() && done.unwrap() >= 0);
        let st = focuscore_timer_state();
        assert!(matches!(st.kind, crate::models::TimerStateKind::Idle));
    }

    #[test]
    fn time_entry_round_trip() {
        open_test_db();
        let task_id = focuscore_quick_add("parent task".into(), None).unwrap();
        let id = focuscore_insert_time_entry(
            "e1".into(),
            task_id.clone(),
            TimerMode::Pomodoro,
            1000,
            1600,
            60,
            1000,
            1600,
        )
        .unwrap();
        assert_eq!(id, "e1");
        let entries = focuscore_list_time_entries(task_id);
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].duration_sec, 60);
    }
}
