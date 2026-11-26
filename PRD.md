# Project: "FocusCore" — Native Cross-Platform Task, Timeboxing & Pomodoro Suite

## 1. Objective & Philosophy
Build a local-first, ultra-low-resource personal productivity suite inspired by Super Productivity, focused strictly on daily execution: tasks, subtasks, markdown notes, daily timeboxing, and task-linked Pomodoro/time tracking.
- **Zero WebViews:** No Electron, Tauri, or browser wrappers. Every client must use native OS UI toolkits (<40 MB idle RAM, near-zero idle CPU).
- **Deep OS Shell Integration:** Each platform must feel like a first-class system utility (Menu Bar popover on macOS, System Tray + Windows Notifications on Windows, Sticky Foreground Notification + Alarm reminders on Android).
- **Offline-First & Multi-Device Sync:** Instant local SQLite read/write with background delta-sync to a lightweight single-user Go server.

---

## 2. Monorepo & Architecture

```text
/
├── core/                 # Rust shared library (SQLite DB, models, timer engine, sync client)
├── server/               # Go single-binary REST sync server + SQLite
├── clients/
│   ├── windows/          # C# + WinUI 3 (Windows App SDK) — Build First on Windows
│   ├── android/          # Kotlin + Jetpack Compose — Build Second on Windows
│   └── macos/            # Swift + SwiftUI — Built via GitHub Actions / macOS
└── .github/workflows/    # Automated cross-platform & macOS cloud build CI
```

### Shared Core (`/core` — Rust)
- **Bindings:** Uses `uniffi-rs` to auto-generate idiomatic C#, Kotlin, and Swift bindings from Rust structs/traits.
- **Storage:** `rusqlite` in WAL mode.
- **Deterministic Timer Engine:** Calculates remaining/elapsed time from monotonic UTC timestamps (`started_at`, `paused_duration`) rather than relying on fragile background sleep loops. Exposes lightweight state snapshots (`Idle`, `Running { task_id, mode, remaining_sec }`, `Paused`) so OS trays/notifications can render live countdowns cleanly.
- **Sync Engine:** Delta-sync over HTTPS (`POST /sync`) using Last-Write-Wins (LWW) conflict resolution (`updated_at` UTC ms) and soft deletes (`deleted_at`).

### Sync Server (`/server` — Go)
- Single static binary using Go standard library `net/http` + `modernc.org/sqlite` (pure Go, CGO-free).
- Static Bearer token auth (`SYNC_AUTH_TOKEN` env var).
- Endpoints: `POST /sync` (push dirty client rows, return server rows where `updated_at > client_last_sync`), `GET /health`.

---

## 3. Core Functional Requirements

1. **Task & Inbox Management:**
   - Global quick-add for one-off tasks into Backlog/Inbox or Today.
   - Single-level nested subtasks with completion toggles.
   - Plaintext/Markdown notes pane per parent task.
2. **Daily Timeboxing:**
   - Daily schedule timeline (06:00–23:00 in 30-min slots).
   - Assign start time + duration (15m, 30m, 45m, 1h, etc.) to Today's tasks to render timebox blocks.
3. **Task-Linked Pomodoro & Time Tracking:**
   - Start/Pause/Stop Pomodoro (configurable work/short-break/long-break) or open-ended Stopwatch directly on any task.
   - Automatically logs completed/stopped intervals to `time_entries` and aggregates daily/task totals.

---

## 4. Platform-Specific Native OS Integrations (Mandatory)

### A. Windows Client (`/clients/windows` — C# & WinUI 3)
- **System Tray (Notification Area) Icon:** Persistent tray icon (using `H.NotifyIcon.WinUI` or native Shell_NotifyIcon) displaying active timer status on hover, left-click to toggle quick-control flyout, right-click menu (`Pause/Resume`, `Complete Task`, `Quick Add`, `Quit`).
- **Taskbar Integration:** Taskbar badge/progress state (`ITaskbarList3`) showing Pomodoro progress bar directly on the Windows taskbar icon, plus Taskbar Jump List actions (`Quick Add Task`, `Start Focus`).
- **Toast Notifications & Reminders:** Native Windows App SDK `AppNotificationBuilder` interactive toasts when a Pomodoro finishes (with action buttons: `Start Break`, `Extend 5m`, `Mark Done`) or when a scheduled timebox starts.
- **Global Hotkey:** System-wide shortcut (e.g., `Ctrl+Shift+Space`) to summon a lightweight quick-add task dialog from anywhere in Windows.

### B. Android Client (`/clients/android` — Kotlin & Jetpack Compose)
- **Sticky Foreground Service Notification:** Ongoing, high-priority `ForegroundService` notification (`NotificationCompat.MediaStyle` or custom chronometer) while a Pomodoro/Stopwatch is active—displaying live countdown (`setUsesChronometer(true)` / `setChronometerCountDown(true)`), active task title, and inline action buttons (`Pause`, `Stop`, `Done`) so Android never kills the timer in the background.
- **Exact Timebox Reminders:** Uses `AlarmManager` (`SCHEDULE_EXACT_ALARM`) to trigger reliable notifications right when a scheduled daily timebox block begins.
- **Quick Settings Tile & Home Screen Widget:** Android `TileService` to start/pause focus from the notification shade, plus a lightweight Glance (`androidx.glance`) home screen widget showing Today's top tasks.

### C. macOS Client (`/clients/macos` — Swift & SwiftUI)
- **Menu Bar Extra (`MenuBarExtra` / `NSStatusItem`):** Native menu bar icon displaying the live `MM:SS` countdown and truncated active task title directly in the macOS top bar. Clicking opens a native SwiftUI popover with timer controls, Today's task list, and quick-add input without needing to open the main window.
- **Native macOS Notifications:** `UNUserNotificationCenter` actionable notifications for Pomodoro completion and upcoming timebox blocks.
- **Global Shortcut:** System-wide hotkey to open the menu bar quick-add popover.

---

## 5. Shared SQLite Schema (UUIDv7 PKs, Unix Epoch ms UTC)

```sql
CREATE TABLE tasks (
    id TEXT PRIMARY KEY,
    parent_id TEXT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    notes TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'todo', -- 'todo' | 'done'
    scheduled_date TEXT NULL,            -- 'YYYY-MM-DD', NULL = backlog/inbox
    timebox_start_min INTEGER NULL,      -- Minutes from midnight (e.g., 540 = 09:00)
    timebox_duration_min INTEGER NULL,   -- Planned duration in minutes
    reminder_enabled INTEGER NOT NULL DEFAULT 1,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    deleted_at INTEGER NULL,
    is_dirty INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE time_entries (
    id TEXT PRIMARY KEY,
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    mode TEXT NOT NULL,                  -- 'pomodoro' | 'stopwatch'
    started_at INTEGER NOT NULL,
    ended_at INTEGER NOT NULL,
    duration_sec INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    deleted_at INTEGER NULL,
    is_dirty INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE sync_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
```

---

## 6. Execution Milestones (Windows Development Host)

Execute strictly in order. Verify compilation and tests before moving to the next milestone:

1. **Milestone 1 — Go Sync Server (`/server`):** Build Go binary, SQLite schema, Bearer auth, `POST /sync` handler, and conflict-resolution tests.
2. **Milestone 2 — Rust Shared Core (`/core`):** Implement SQLite store, timer state machine, sync client, and `uniffi` bindings (`C#`, `Kotlin`, `Swift`). Run `cargo test` on Windows.
3. **Milestone 3 — Windows Native Client (`/clients/windows`):** Build WinUI 3 desktop app, System Tray flyout, Taskbar progress indicator, interactive Toast notifications, and global quick-add hotkey.
4. **Milestone 4 — Android Native Client (`/clients/android`):** Build Jetpack Compose app, Foreground Service sticky timer notification, `AlarmManager` timebox reminders, and Glance widget.
5. **Milestone 5 — macOS Native Client + CI (`/clients/macos` & `.github/workflows/macos.yml`):** Write SwiftUI app + `MenuBarExtra` popover + `UNUserNotificationCenter` integration, and create a GitHub Actions workflow (`macos-latest`) that compiles the Rust `.xcframework` and packages the macOS `.app` bundle automatically.

**First Action:** Inspect the local Windows environment (`rustc`, `cargo`, `go`, `dotnet`, `java`/`gradle`), report any missing SDKs, and start implementing **Milestone 1 (Go Sync Server)** and **Milestone 2 (Rust Shared Core)**.
