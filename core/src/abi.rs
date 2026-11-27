//! C ABI facade consumed by C# via csbindgen-generated P/Invoke bindings.
//!
//! These live in their own module so their `focuscore_*` names don't collide
//! with the UniFFI free functions of the same name in the crate root.

use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int, c_void};
use std::ptr;

use crate::models::{TimerMode, TimerStateKind};
use crate::{time_now_ms, with_core, Core, CORE};

// ----- C ABI types (repr(C) so the C# P/Invoke struct layout matches) -----

/// Mirror of `TimerStateKind` as a plain C enum.
#[repr(C)]
pub enum TimerStateKindAbi {
    Idle = 0,
    Running = 1,
    Paused = 2,
}

/// Mirror of `TimerMode` as a plain C enum.
#[repr(C)]
pub enum TimerModeAbi {
    Pomodoro = 0,
    Stopwatch = 1,
}

/// Mirror of `TimerState` returned by value from `focuscore_timer_state()`.
///
/// `task_id` is a null-terminated UTF-8 string allocated with `CString::into_raw`;
/// the caller must copy it and then call `focuscore_string_free` to release it.
#[repr(C)]
pub struct TimerStateAbi {
    pub kind: c_int,            // 0 Idle, 1 Running, 2 Paused
    pub task_id: *const c_char, // null when none
    pub mode: c_int,            // -1 = none, otherwise a TimerModeAbi discriminant
    pub remaining_sec: c_int,
    pub elapsed_sec: c_int,
}

/// Release a `*const c_char` / `*mut c_char` previously returned by a focuscore_* function.
#[no_mangle]
pub extern "C" fn focuscore_string_free(ptr: *mut c_void) {
    if !ptr.is_null() {
        unsafe {
            drop(CString::from_raw(ptr as *mut c_char));
        }
    }
}

fn cstr_to_string(ptr: *const c_char) -> String {
    if ptr.is_null() {
        return String::new();
    }
    unsafe { CStr::from_ptr(ptr).to_string_lossy().into_owned() }
}

// ----- timer engine -----

#[no_mangle]
pub extern "C" fn focuscore_timer_state() -> TimerStateAbi {
    let st = with_core(|core| core.timer.lock().unwrap().state());
    let task_id_ptr = st
        .task_id
        .as_deref()
        .map(|s| CString::new(s).unwrap().into_raw() as *const c_char)
        .unwrap_or(ptr::null());
    let mode = match st.mode {
        Some(TimerMode::Pomodoro) => TimerModeAbi::Pomodoro as c_int,
        Some(TimerMode::Stopwatch) => TimerModeAbi::Stopwatch as c_int,
        None => -1,
    };
    let kind = match st.kind {
        TimerStateKind::Idle => TimerStateKindAbi::Idle as c_int,
        TimerStateKind::Running => TimerStateKindAbi::Running as c_int,
        TimerStateKind::Paused => TimerStateKindAbi::Paused as c_int,
    };
    TimerStateAbi {
        kind,
        task_id: task_id_ptr,
        mode,
        remaining_sec: st.remaining_sec,
        elapsed_sec: st.elapsed_sec,
    }
}

#[no_mangle]
pub extern "C" fn focuscore_timer_start(task_id: *const c_char, mode: c_int, total_sec: c_int) {
    let task_id = cstr_to_string(task_id);
    let mode = match mode {
        1 => TimerMode::Stopwatch,
        _ => TimerMode::Pomodoro,
    };
    with_core(|core| {
        let mut t = core.timer.lock().unwrap();
        t.start(task_id, mode, total_sec);
    });
}

#[no_mangle]
pub extern "C" fn focuscore_timer_pause() {
    with_core(|core| core.timer.lock().unwrap().pause());
}

#[no_mangle]
pub extern "C" fn focuscore_timer_resume() {
    with_core(|core| core.timer.lock().unwrap().resume());
}

#[no_mangle]
pub extern "C" fn focuscore_timer_reset() {
    with_core(|core| core.timer.lock().unwrap().reset());
}

#[no_mangle]
pub extern "C" fn focuscore_timer_stop() -> c_int {
    with_core(|core| core.timer.lock().unwrap().stop()).unwrap_or(-1)
}

// ----- time entries -----

#[no_mangle]
pub extern "C" fn focuscore_insert_time_entry(
    id: *const c_char,
    task_id: *const c_char,
    mode: c_int,
    started_at: i64,
    ended_at: i64,
    duration_sec: i32,
    created_at: i64,
    updated_at: i64,
) -> *const c_char {
    let id = cstr_to_string(id);
    if id.is_empty() {
        return std::ptr::null::<c_char>();
    }
    let task_id = cstr_to_string(task_id);
    let mode = match mode {
        1 => TimerMode::Stopwatch,
        _ => TimerMode::Pomodoro,
    };
    let res = with_core(|_| {
        crate::focuscore_insert_time_entry(
            id.clone(),
            task_id,
            mode,
            started_at,
            ended_at,
            duration_sec,
            created_at,
            updated_at,
        )
    });
    match res {
        Ok(_) => CString::new(id).unwrap().into_raw() as *const c_char,
        Err(_) => std::ptr::null::<c_char>(),
    }
}

#[no_mangle]
pub extern "C" fn focuscore_list_time_entries(task_id: *const c_char) -> *const c_char {
    let task_id = cstr_to_string(task_id);
    let entries = with_core(|core| {
        core.store
            .list_time_entries_for_task(&task_id)
            .unwrap_or_default()
    });
    let mut s = String::from("[");
    for (i, e) in entries.iter().enumerate() {
        if i > 0 {
            s.push(',');
        }
        s.push_str("{\"id\":\"");
        s.push_str(&escape_json(&e.id));
        s.push_str("\",\"task_id\":\"");
        s.push_str(&escape_json(&e.task_id));
        s.push_str("\",\"mode\":\"");
        s.push_str(if e.mode == TimerMode::Pomodoro {
            "pomodoro"
        } else {
            "stopwatch"
        });
        s.push_str("\",\"started_at\":");
        s.push_str(&e.started_at.to_string());
        s.push_str(",\"ended_at\":");
        s.push_str(&e.ended_at.to_string());
        s.push_str(",\"duration_sec\":");
        s.push_str(&e.duration_sec.to_string());
        s.push_str(",\"created_at\":");
        s.push_str(&e.created_at.to_string());
        s.push_str(",\"updated_at\":");
        s.push_str(&e.updated_at.to_string());
        s.push('}');
    }
    s.push(']');
    CString::new(s).unwrap().into_raw() as *const c_char
}

fn escape_json(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    for ch in s.chars() {
        match ch {
            '\\' => out.push_str("\\\\"),
            '"' => out.push_str("\\\""),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if c.is_control() => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out
}

// ----- sync -----

#[no_mangle]
pub extern "C" fn focuscore_sync(base_url: *const c_char, token: *const c_char) -> c_int {
    let base_url = cstr_to_string(base_url);
    let token = cstr_to_string(token);
    match with_core(|_| crate::focuscore_sync(base_url, token)) {
        Ok(_) => 0,
        Err(_) => 1,
    }
}

#[no_mangle]
pub extern "C" fn focuscore_version() -> *const c_char {
    c"focuscore 0.1.0".as_ptr()
}

#[no_mangle]
pub extern "C" fn focuscore_open(db_path: *const c_char) -> i32 {
    let path = if db_path.is_null() {
        None
    } else {
        unsafe {
            std::ffi::CStr::from_ptr(db_path)
                .to_str()
                .ok()
                .map(|s| s.to_string())
        }
    };
    match Core::open_with_path(path.as_deref()) {
        Ok(core) => {
            let mut guard = CORE.lock().unwrap();
            *guard = Some(core);
            0
        }
        Err(_) => 1,
    }
}

#[no_mangle]
pub extern "C" fn focuscore_close() {
    let mut guard = CORE.lock().unwrap();
    *guard = None;
}

#[no_mangle]
pub extern "C" fn focuscore_quick_add(
    title: *const c_char,
    scheduled_date: *const c_char,
) -> *const c_char {
    let title = unsafe {
        std::ffi::CStr::from_ptr(title)
            .to_string_lossy()
            .into_owned()
    };
    let date = if scheduled_date.is_null() {
        None
    } else {
        Some(unsafe {
            std::ffi::CStr::from_ptr(scheduled_date)
                .to_string_lossy()
                .into_owned()
        })
    };
    let now = time_now_ms();
    let id = uuid::Uuid::now_v7().to_string();
    let task = crate::models::Task {
        id: id.clone(),
        parent_id: None,
        title,
        notes: String::new(),
        status: "todo".into(),
        scheduled_date: date,
        timebox_start_min: None,
        timebox_duration_min: None,
        reminder_enabled: true,
        sort_order: 0,
        created_at: now,
        updated_at: now,
        deleted_at: None,
    };
    let res = with_core(|core| core.store.insert_task(&task));
    match res {
        Ok(_) => {
            let out = format!("{}\0", id);
            let cstr = std::ffi::CString::new(out).unwrap();
            cstr.into_raw() as *const c_char
        }
        Err(_) => std::ptr::null::<c_char>(),
    }
}
