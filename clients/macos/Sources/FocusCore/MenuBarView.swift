import SwiftUI

/// SwiftUI popover shown from the menu bar (and from the global hotkey via PopoverCoordinator).
///
/// Reads timer state from FocusCoreService (which polls the core every second) and uses a
/// `Timer.publish` tick for a smooth MM:SS countdown display, re-syncing with the service on
/// every state change.
struct MenuBarView: View {
    @EnvironmentObject var service: FocusCoreService
    @EnvironmentObject var coordinator: PopoverCoordinator

    @State private var quickAddTitle = ""
    @State private var tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()
    @State private var displaySeconds: Int = 0

    private var activeTask: Task? {
        guard let id = service.timerState.taskId else { return nil }
        return service.tasks.first { $0.id == id }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            // MARK: Header — countdown + active task title
            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text(formatted(seconds: displaySeconds))
                        .font(.system(size: 22, weight: .bold, design: .monospaced))
                    Spacer(minLength: 8)
                    Text(service.timerState.kind.rawValue.capitalized)
                        .font(.caption2).bold()
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(Capsule().fill(Color.accentColor.opacity(0.15)))
                        .foregroundColor(.accentColor)
                }
                if let task = activeTask {
                    Text(task.title)
                        .font(.headline)
                        .lineLimit(1)
                        .truncationMode(.middle)
                } else {
                    Text("No active task")
                        .font(.headline)
                        .foregroundColor(.secondary)
                }
            }

            Divider()

            // MARK: Timer controls
            HStack(spacing: 8) {
                Button("Start") {
                    if let task = activeTask {
                        service.startTimer(taskId: task.id, mode: service.timerState.mode ?? .pomodoro)
                    }
                }
                .disabled(service.timerState.kind == .running || activeTask == nil)
                Button("Pause") { service.pauseTimer() }
                .disabled(service.timerState.kind != .running)
                Button("Stop") { service.stopTimer() }
                .disabled(service.timerState.kind == .idle)
            }
            .buttonStyle(.borderless)
            .controlSize(.small)

            Divider()

            // MARK: Today's task list
            VStack(alignment: .leading, spacing: 6) {
                Text("Today").font(.headline)
                if todayTasks.isEmpty {
                    Text("No tasks scheduled for today.")
                        .font(.caption).foregroundColor(.secondary)
                } else {
                    ForEach(todayTasks) { task in
                        TodayRow(task: task)
                    }
                }
            }

            Divider()

            // MARK: Quick add
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    TextField("Quick-add a task", text: $quickAddTitle)
                        .textFieldStyle(.roundedBorder)
                    Button("Add") {
                        service.quickAdd(title: quickAddTitle)
                        quickAddTitle = ""
                    }
                    .disabled(quickAddTitle.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    .controlSize(.small)
                }
                Text("Press Control+Shift+Space to open this popover from anywhere.")
                    .font(.caption2).foregroundColor(.secondary)
            }

            Divider()

            // MARK: Sync status + quit
            HStack(spacing: 6) {
                Circle().fill(syncColor).frame(width: 6, height: 6)
                Text(syncLabel)
                    .font(.caption2).foregroundColor(.secondary)
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
                    .controlSize(.small).buttonStyle(.borderless)
            }
        }
        .padding(12)
        .frame(width: 320)
        .onAppear { displaySeconds = service.timerState.remainingSec }
        .onReceive(tick) { _ in
            if service.timerState.kind == .running {
                displaySeconds = max(0, displaySeconds - 1)
            } else {
                displaySeconds = service.timerState.remainingSec
            }
        }
        .onChange(of: service.timerState.remainingSec) { newValue in
            displaySeconds = newValue
        }
    }

    // MARK: Today's tasks (scheduled for today, not deleted)
    private var todayTasks: [Task] {
        let cal = Calendar.current
        return service.tasks.filter {
            guard let date = $0.scheduledDate, $0.deletedAt == nil else { return false }
            return cal.isDateInToday(date)
        }.sorted { ($0.sortOrder, $0.title) < ($1.sortOrder, $1.title) }
    }

    // MARK: Sync status
    private var syncColor: Color {
        switch service.syncStatus {
        case .connected: return .green
        case .syncing:  return .yellow
        case .error:    return .red
        case .disconnected: return .gray
        }
    }

    private var syncLabel: String {
        switch service.syncStatus {
        case .connected: return "Synced"
        case .syncing:   return "Syncing…"
        case .error:     return "Sync error"
        case .disconnected: return "Disconnected"
        }
    }

    private func formatted(seconds: Int) -> String {
        let total = max(0, seconds)
        return String(format: "%02d:%02d", total / 60, total % 60)
    }
}

// MARK: - Today row

private struct TodayRow: View {
    let task: Task

    var body: some View {
        HStack(spacing: 8) {
            Circle().fill(Color.secondary.opacity(0.4)).frame(width: 7, height: 7)
            VStack(alignment: .leading, spacing: 1) {
                Text(task.title).font(.callout).lineLimit(1)
                if let startMin = task.timeboxStartMin {
                    Text(timeString(minutes: startMin)
                        + (task.timeboxDurationMin.map { " • \($0) min" } ?? ""))
                        .font(.caption2).foregroundColor(.secondary)
                }
            }
            Spacer()
        }
        .contentShape(Rectangle())
    }

    private func timeString(minutes: Int) -> String {
        String(format: "%d:%02d", minutes / 60, minutes % 60)
    }
}
