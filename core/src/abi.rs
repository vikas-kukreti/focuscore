//! C ABI facade consumed by C# via csbindgen-generated P/Invoke bindings.
//!
//! These live in their own module so their `focuscore_*` names don't collide
//! with the UniFFI free functions of the same name in the crate root.

use crate::{time_now_ms, with_core, Core, CORE};

#[no_mangle]
pub extern "C" fn focuscore_version() -> *const i8 {
    c"focuscore 0.1.0".as_ptr()
}

#[no_mangle]
pub extern "C" fn focuscore_open(db_path: *const i8) -> i32 {
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
pub extern "C" fn focuscore_quick_add(title: *const i8, scheduled_date: *const i8) -> *const i8 {
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
            cstr.into_raw() as *const i8
        }
        Err(_) => c"".as_ptr(),
    }
}
