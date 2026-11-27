import Cocoa
import Foundation

// MARK: - Data models
//
// These local models mirror `core/interface.udl`. On CI they are replaced by the FocusCore
// UniFFI Swift bindings generated from `core/interface.udl` by `uniffi-bindgen-swift`
// (package `FocusCore`). Until then these are placeholders so the SwiftUI target compiles.

public enum TimerMode: String, Codable, Sendable, CaseIterable {
    case pomodoro   = "Pomodoro"
    case timebox    = "Timebox"
    case shortBreak = "ShortBreak"
    case longBreak  = "LongBreak"
}

public enum TimerKind: String, Codable, Sendable {
    case idle, running, paused
}

public struct TimerState: Codable, Sendable, Equatable {
    public var kind: TimerKind
    public var taskId: UUID?
    public var mode: TimerMode?
    public var remainingSec: Int
    public var elapsedSec: Int

    public static let idle = TimerState(kind: .idle, taskId: nil, mode: nil,
                                        remainingSec: 0, elapsedSec: 0)
}

// Renamed from `Task` to `TaskModel` to avoid conflict with Swift Concurrency `Task`
public struct TaskModel: Identifiable, Codable, Sendable, Equatable {
    public var id: UUID
    public var parentId: UUID?
    public var title: String
    public var notes: String
    public var status: String
    public var scheduledDate: Date?
    public var timeboxStartMin: Int?    // minutes since midnight (local time)
    public var timeboxDurationMin: Int?
    public var reminderEnabled: Bool
    public var sortOrder: Int
    public var createdAt: Date
    public var updatedAt: Date
    public var deletedAt: Date?
}

// MARK: - FocusCoreService
//
// Swift singleton wrapping the FocusCore C ABI (libfocuscore.a) and, on CI, the UniFFI
// Swift bindings. Opens the local database and polls timer state, publishing updates
// to SwiftUI via @Published.

@MainActor
public final class FocusCoreService: ObservableObject {
    public static let shared = FocusCoreService()

    public enum SyncStatus: String, Sendable {
        case disconnected, connected, syncing, error
    }

    @Published public private(set) var timerState: TimerState = .idle
    @Published public private(set) var tasks: [TaskModel] = []
    @Published public private(set) var isOpen: Bool = false
    @Published public private(set) var syncStatus: SyncStatus = .disconnected
    @Published public private(set) var errorMessage: String? = nil

    /// ~/Library/Application Support/FocusCore/focuscore.db
    public let dbURL: URL

    private var pollTask: Swift.Task<Void, Never>?

    private init() {
        if let support = FileManager.default.urls(for: .applicationSupportDirectory,
                                                  in: .userDomainMask).first {
            self.dbURL = support
                .appendingPathComponent("FocusCore", isDirectory: true)
                .appendingPathComponent("focuscore.db")
        } else {
            self.dbURL = URL(fileURLWithPath: NSTemporaryDirectory())
                .appendingPathComponent("FocusCore/focuscore.db")
        }
    }

    public func open() {
        do {
            try FileManager.default.createDirectory(
                at: dbURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            let rc = dbURL.path.withCString { focuscore_open($0) }
            guard rc == 0 else { throw FocusCoreError.openFailed(rc) }
            isOpen = true
            syncStatus = .connected
            errorMessage = nil
            startPolling()
            Task { await refreshTasks() }
        } catch {
            syncStatus = .error
            errorMessage = error.localizedDescription
        }
    }

    public func close() {
        stopPolling()
        let rc = focuscore_close()
        if rc != 0 {
            errorMessage = "Failed to close FocusCore database (code \(rc))"
        }
        isOpen = false
        syncStatus = .disconnected
        timerState = .idle
    }

    public func quickAdd(title: String) {
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        let rc = trimmed.withCString { focuscore_quick_add($0) }
        if rc == 0 {
            Task { await refreshTasks() }
        } else {
            errorMessage = "Failed to add task (code \(rc))"
        }
    }

    public func startTimer(taskId: UUID, mode: TimerMode) {
        // TODO: call UniFFI `start_timer(taskId, mode)` once bindings are generated.
        Task { await refreshTimerState() }
    }

    public func pauseTimer() {
        // TODO: call UniFFI `pause_timer()`.
        Task { await refreshTimerState() }
    }

    public func stopTimer() {
        // TODO: call UniFFI `stop_timer()`.
        timerState = .idle
    }

    public func refreshTasks() async {
        // TODO: call UniFFI `list_tasks()` and assign to `self.tasks`.
        // Placeholder: tasks remain empty until bindings are wired.
    }

    public func refreshTimerState() async {
        // TODO: call UniFFI `timer_state()` and assign to `self.timerState`.
        // Placeholder: state is unchanged until bindings are wired.
    }

    // MARK: - Private

    private func startPolling() {
        stopPolling()
        pollTask = Swift.Task {
            while !Swift.Task.isCancelled {
                if let self = self {
                    await self.refreshTimerState()
                }
                try? await Swift.Task.sleep(nanoseconds: 1_000_000_000) // 1 second
            }
        }
    }

    private func stopPolling() {
        pollTask?.cancel()
        pollTask = nil
    }

    deinit {
        stopPolling()
    }
}

public enum FocusCoreError: LocalizedError {
    case openFailed(Int32)
    case notOpen

    public var errorDescription: String? {
        switch self {
        case .openFailed(let rc):
            return "Failed to open FocusCore database (code \(rc))"
        case .notOpen:
            return "FocusCore database is not open"
        }
    }
}

// MARK: - C ABI (libfocuscore.a)
//
// TODO: Import these from the focuscore C header shipped with `libfocuscore.a`
// (aarch64 + x86_64 static libs built by CI). Link the static lib into this target on CI
// (e.g. linker flags or an `.systemLibrary` target with a module map).
//
// These declarations are placeholders until the real header is available.

@_silgen_name("focuscore_version")
private func focuscore_version() -> UnsafePointer<Int8>

@_silgen_name("focuscore_open")
private func focuscore_open(_ dbPath: UnsafePointer<Int8>) -> Int32

@_silgen_name("focuscore_close")
private func focuscore_close() -> Int32

@_silgen_name("focuscore_quick_add")
private func focuscore_quick_add(_ title: UnsafePointer<Int8>) -> Int32