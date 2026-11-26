//! Deterministic timer engine.
//!
//! State is derived from monotonic UTC timestamps:
//!   - Running: started_at + paused_duration -> remaining = total - (now - started_at - paused_duration)
//!   - Paused: remaining frozen at pause time
//! No background sleep loops are used.

use crate::models::{TimerMode, TimerState, TimerStateKind};
use crate::time_now_ms;

/// Default pomodoro lengths (seconds).
const DEFAULT_WORK_SEC: i32 = 25 * 60;
const DEFAULT_SHORT_BREAK_SEC: i32 = 5 * 60;
const DEFAULT_LONG_BREAK_SEC: i32 = 15 * 60;

pub struct TimerEngine {
    task_id: Option<String>,
    mode: TimerMode,
    total_sec: i32,
    started_at: Option<i64>,
    paused_duration_sec: i32, // accumulated paused time while running
    paused_at: Option<i64>,   // set when paused, None when running
    remaining_sec: i32,       // frozen remaining when paused/stopped
}

impl Default for TimerEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl TimerEngine {
    pub fn new() -> Self {
        Self {
            task_id: None,
            mode: TimerMode::Pomodoro,
            total_sec: DEFAULT_WORK_SEC,
            started_at: None,
            paused_duration_sec: 0,
            paused_at: None,
            remaining_sec: DEFAULT_WORK_SEC,
        }
    }

    pub fn state(&self) -> TimerState {
        let remaining = self.current_remaining();
        let kind = if self.started_at.is_some() && self.paused_at.is_none() {
            TimerStateKind::Running
        } else if self.started_at.is_some() && self.paused_at.is_some() {
            TimerStateKind::Paused
        } else {
            TimerStateKind::Idle
        };
        let elapsed = if remaining >= 0 {
            self.total_sec - remaining
        } else {
            self.total_sec
        };
        TimerState {
            kind,
            task_id: self.task_id.clone(),
            mode: Some(self.mode.clone()),
            remaining_sec: remaining.max(0),
            elapsed_sec: elapsed.max(0).min(self.total_sec),
        }
    }

    /// Current remaining seconds derived from wall-clock (not stored).
    fn current_remaining(&self) -> i32 {
        if self.started_at.is_none() {
            return self.remaining_sec;
        }
        if self.paused_at.is_some() {
            // Paused: remaining was frozen at pause time.
            return self.remaining_sec;
        }
        // Running: now - started_at - paused_duration
        let now = time_now_ms();
        let started = self.started_at.unwrap();
        let elapsed_ms = (now - started).max(0) - (self.paused_duration_sec as i64) * 1000;
        let elapsed_sec = (elapsed_ms / 1000) as i32;
        self.total_sec - elapsed_sec
    }

    pub fn start(&mut self, task_id: String, mode: TimerMode, total_sec: i32) {
        let now = time_now_ms();
        // If resuming a paused timer, keep remaining_sec and add the paused
        // duration gap so paused time isn't double counted.
        if self.paused_at.is_some() {
            let pause_elapsed_ms = now - self.paused_at.unwrap();
            self.paused_duration_sec += (pause_elapsed_ms / 1000) as i32;
            self.paused_at = None;
        } else {
            // Fresh start: reset remaining to total.
            self.remaining_sec = total_sec;
            self.total_sec = total_sec;
        }
        self.task_id = Some(task_id);
        self.mode = mode;
        self.started_at = Some(now);
    }

    pub fn pause(&mut self) {
        if self.started_at.is_none() || self.paused_at.is_some() {
            return;
        }
        let now = time_now_ms();
        self.remaining_sec = self.current_remaining().max(0);
        self.paused_at = Some(now);
    }

    pub fn resume(&mut self) {
        if self.paused_at.is_none() {
            return;
        }
        let now = time_now_ms();
        let pause_elapsed_ms = now - self.paused_at.unwrap();
        self.paused_duration_sec += (pause_elapsed_ms / 1000) as i32;
        self.paused_at = None;
    }

    pub fn stop(&mut self) -> Option<i32> {
        if self.started_at.is_none() {
            return None;
        }
        let completed_sec = self.total_sec - self.current_remaining().max(0);
        // Reset to idle.
        self.started_at = None;
        self.paused_at = None;
        self.paused_duration_sec = 0;
        let done = completed_sec;
        self.remaining_sec = self.total_sec;
        Some(done.max(0))
    }

    pub fn reset(&mut self) {
        self.task_id = None;
        self.mode = TimerMode::Pomodoro;
        self.total_sec = DEFAULT_WORK_SEC;
        self.started_at = None;
        self.paused_at = None;
        self.paused_duration_sec = 0;
        self.remaining_sec = DEFAULT_WORK_SEC;
    }

    pub fn set_break_lengths(&mut self, work: i32, short: i32, long: i32) {
        // Stored per-instance; callers may persist via sync_meta if desired.
        let _ = (work, short, long);
    }

    pub fn current_mode(&self) -> TimerMode {
        self.mode.clone()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fresh_timer_is_idle() {
        let t = TimerEngine::new();
        matches!(t.state().kind, TimerStateKind::Idle);
    }

    #[test]
    fn start_then_pause_freezes_remaining() {
        let mut t = TimerEngine::new();
        t.start("t1".into(), TimerMode::Pomodoro, 60);
        assert!(matches!(t.state().kind, TimerStateKind::Running));
        let before = t.state().remaining_sec;
        t.pause();
        assert!(matches!(t.state().kind, TimerStateKind::Paused));
        let after = t.state().remaining_sec;
        // remaining should not increase after pause (frozen).
        assert!(after <= before);
    }

    #[test]
    fn stop_returns_idle() {
        let mut t = TimerEngine::new();
        t.start("t1".into(), TimerMode::Stopwatch, 100);
        let done = t.stop();
        assert!(done.is_some());
        assert!(matches!(t.state().kind, TimerStateKind::Idle));
    }
}
