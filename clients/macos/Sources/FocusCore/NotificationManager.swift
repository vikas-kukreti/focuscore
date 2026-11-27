import Foundation
import UserNotifications

/// Manages UNUserNotificationCenter authorization, actionable categories, and triggers.
///
/// - Pomodoro completion → `pomodoro-complete` category (Start Break / Extend 5m / Mark Done)
/// - Upcoming timebox blocks → `timebox-start` category via UNCalendarNotificationTrigger
public final class NotificationManager: NSObject, UNUserNotificationCenterDelegate {
    public static let shared = NotificationManager()

    private let center = UNUserNotificationCenter.current()

    private override init() {
        super.init()
        center.delegate = self
    }

    /// Request notification authorization and register actionable categories.
    public func requestAuthorization() async -> Bool {
        let granted = (try? await center.requestAuthorization(options: [.alert, .sound, .badge])) ?? false
        registerCategories()
        return granted
    }

    public func registerCategories() {
        let pomodoroComplete = UNNotificationCategory(
            identifier: "pomodoro-complete",
            actions: [
                UNNotificationAction(identifier: "start-break", title: "Start Break", options: []),
                UNNotificationAction(identifier: "extend-5m", title: "Extend 5m", options: []),
                UNNotificationAction(identifier: "mark-done", title: "Mark Done",
                                     options: [.authenticationRequired])
            ],
            intentIdentifiers: [],
            hiddenPreviewsBodyPlaceholder: "",
            categorySummaryFormat: nil,
            options: [.customDismissAction]
        )

        let timeboxStart = UNNotificationCategory(
            identifier: "timebox-start",
            actions: [
                UNNotificationAction(identifier: "snooze-5m", title: "Snooze 5m", options: []),
                UNNotificationAction(identifier: "view-task", title: "View Task",
                                     options: [.foreground])
            ],
            intentIdentifiers: [],
            hiddenPreviewsBodyPlaceholder: "",
            categorySummaryFormat: nil,
            options: []
        )

        center.setNotificationCategories([pomodoroComplete, timeboxStart])
    }

    // MARK: - Scheduling

    /// Schedule a one-off notification at a timebox start time.
    public func scheduleTimeboxStart(taskId: UUID, title: String, at date: Date) {
        let content = UNMutableNotificationContent()
        content.title = "Timebox starting"
        content.body = title
        content.categoryIdentifier = "timebox-start"
        content.sound = .default
        content.userInfo = ["taskId": taskId.uuidString]

        let components = Calendar.current.dateComponents(
            [.year, .month, .day, .hour, .minute], from: date
        )
        let trigger = UNCalendarNotificationTrigger(dateMatching: components, repeats: false)
        let request = UNNotificationRequest(
            identifier: "timebox-\(taskId.uuidString)",
            content: content,
            trigger: trigger
        )
        center.add(request) { error in
            if let error = error {
                print("FocusCore: failed to schedule timebox notification: \(error)")
            }
        }
    }

    /// Post an immediate Pomodoro completion notification.
    public func schedulePomodoroCompletion(taskId: UUID, title: String) {
        let content = UNMutableNotificationContent()
        content.title = "Pomodoro complete"
        content.body = title
        content.categoryIdentifier = "pomodoro-complete"
        content.sound = .default
        content.userInfo = ["taskId": taskId.uuidString]
        let request = UNNotificationRequest(
            identifier: "pomodoro-\(taskId.uuidString)",
            content: content,
            trigger: nil
        )
        center.add(request) { error in
            if let error = error {
                print("FocusCore: failed to post pomodoro notification: \(error)")
            }
        }
    }

    // MARK: - UNUserNotificationCenterDelegate

    public func userNotificationCenter(_ center: UNUserNotificationCenter,
                                       willPresent notification: UNNotification,
                                       withCompletionHandler completionHandler:
                                       @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound, .list])
    }

    public func userNotificationCenter(_ center: UNUserNotificationCenter,
                                       didReceive response: UNNotificationResponse,
                                       withCompletionHandler completionHandler: @escaping () -> Void) {
        let action = response.actionIdentifier
        let taskId = (response.notification.request.content.userInfo["taskId"] as? String)
            .flatMap { UUID(uuidString: $0) }

        // TODO: forward actions to FocusCoreService:
        // start-break / extend-5m / mark-done (pomodoro-complete)
        // snooze-5m / view-task (timebox-start)
        _ = action
        _ = taskId

        completionHandler()
    }
}
