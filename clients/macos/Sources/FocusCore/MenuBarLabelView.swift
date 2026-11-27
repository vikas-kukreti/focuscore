import SwiftUI

/// Menu bar label: live MM:SS countdown + truncated active task title.
struct MenuBarLabelView: View {
    @EnvironmentObject var service: FocusCoreService

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "timer")
                .font(.system(size: 12, weight: .semibold))

            if service.timerState.kind != .idle, let task = activeTask {
                Text(formatted(seconds: service.timerState.remainingSec))
                    .font(.system(size: 11, weight: .bold, design: .monospaced))
                Text(task.title)
                    .font(.caption2)
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .frame(width: 92, alignment: .leading)
            } else {
                Text("FocusCore")
                    .font(.caption2)
                    .bold()
            }
        }
    }

    private var activeTask: TaskModel? {
        guard let id = service.timerState.taskId else { return nil }
        return service.tasks.first { $0.id == id }
    }

    private func formatted(seconds: Int) -> String {
        let total = max(0, seconds)
        return String(format: "%02d:%02d", total / 60, total % 60)
    }
}
