//! Shared data models (plain Rust; mirrored by `interface.udl` for UniFFI).

#[derive(Clone, Debug)]
pub struct Task {
    pub id: String,
    pub parent_id: Option<String>,
    pub title: String,
    pub notes: String,
    pub status: String,                 // 'todo' | 'done'
    pub scheduled_date: Option<String>, // 'YYYY-MM-DD'
    pub timebox_start_min: Option<i32>,
    pub timebox_duration_min: Option<i32>,
    pub reminder_enabled: bool,
    pub sort_order: i32,
    pub created_at: i64,
    pub updated_at: i64,
    pub deleted_at: Option<i64>,
}

#[derive(Clone, Debug)]
pub struct TimeEntry {
    pub id: String,
    pub task_id: String,
    pub mode: TimerMode,
    pub started_at: i64,
    pub ended_at: i64,
    pub duration_sec: i32,
    pub created_at: i64,
    pub updated_at: i64,
    pub deleted_at: Option<i64>,
}

#[derive(Clone, Debug, PartialEq)]
pub enum TimerMode {
    Pomodoro,
    Stopwatch,
}

#[derive(Clone, Debug)]
pub struct TimerState {
    pub kind: TimerStateKind,
    pub task_id: Option<String>,
    pub mode: Option<TimerMode>,
    pub remaining_sec: i32,
    pub elapsed_sec: i32,
}

#[derive(Clone, Debug, PartialEq)]
pub enum TimerStateKind {
    Idle,
    Running,
    Paused,
}

#[derive(Clone, Debug)]
pub struct SyncResult {
    pub tasks_pushed: i32,
    pub tasks_pulled: i32,
    pub entries_pushed: i32,
    pub entries_pulled: i32,
}
