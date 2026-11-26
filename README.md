# FocusCore

Native cross-platform personal productivity suite: tasks, subtasks, markdown notes,
daily timeboxing, and task-linked Pomodoro/time tracking.

**Local-first, zero WebViews.** Every client uses native OS UI toolkits
(<40 MB idle RAM, near-zero idle CPU).

```
core/              Rust shared library (SQLite, timer engine, sync client)
server/            Go single-binary REST sync server + SQLite
clients/windows/   C# + WinUI 3
clients/android/   Kotlin + Jetpack Compose
clients/macos/     Swift + SwiftUI
.github/workflows/ Cross-platform CI builds via GitHub Actions
```

## Status

| Milestone | Target | CI |
|---|---|---|
| M0 — Foundation (repo, schema, CI) | done | — |
| M1 — Go sync server | in progress | — |
| M2 — Rust shared core | planned | — |
| M3 — Windows client (WinUI 3) | planned | — |
| M4 — Android client (Compose) | planned | — |
| M5 — macOS client + CI | planned | — |

## Build

Each platform builds on GitHub Actions runners (this host has no macOS/Windows toolchains).
See `.github/workflows/`. Releases attach platform artifacts; download from the
[Releases](https://github.com/vikas-kukreti/focuscore/releases) page to test.

## License

MIT
