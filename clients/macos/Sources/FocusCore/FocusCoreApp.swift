import Cocoa
import SwiftUI

// MARK: - FocusCoreApp

@main
struct FocusCoreApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    init() {
        // Wire the global hotkey (Control+Shift+Space) to open the quick-add popover.
        GlobalHotkey.shared.onTrigger = {
            PopoverCoordinator.shared.openQuickAdd()
        }
    }

    var body: some Scene {
        MenuBarExtra(label: {
            MenuBarLabelView()
        }) {
            MenuBarView()
        }
        .environmentObject(FocusCoreService.shared)
        .environmentObject(PopoverCoordinator.shared)
    }
}

// MARK: - App delegate

/// Menu-bar-only application: LSUIElement is set in Info.plist, so there is no dock
/// icon and no main window. All UI is driven from the menu bar item and the global hotkey.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        FocusCoreService.shared.open()
        Task {
            await NotificationManager.shared.requestAuthorization()
        }
        GlobalHotkey.shared.register()
    }

    func applicationWillTerminate(_ notification: Notification) {
        GlobalHotkey.shared.unregister()
        FocusCoreService.shared.close()
    }
}
