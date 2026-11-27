import Cocoa
import SwiftUI

// MARK: - Global hotkey

/// System-wide hotkey (Control+Shift+Space) that opens the menu bar quick-add popover.
@MainActor
public final class GlobalHotkey {
    public static let shared = GlobalHotkey()

    /// Invoked on the main thread when Control+Shift+Space is pressed.
    public var onTrigger: (() -> Void)?

    private var monitor: Any?

    private init() {}

    public func register() {
        unregister()
        monitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self = self else { return }
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            let isControlShiftSpace = flags.contains(.control)
                && flags.contains(.shift)
                && !flags.contains(.option)
                && !flags.contains(.command)
                && event.keyCode == 49 // Space
            if isControlShiftSpace {
                DispatchQueue.main.async { self.onTrigger?() }
            }
        }
    }

    public func unregister() {
        if let monitor = monitor {
            NSEvent.removeMonitor(monitor)
            self.monitor = nil
        }
    }

    deinit {
        unregister()
    }
}

// MARK: - Popover coordinator

/// Owns the NSPopover used by the global hotkey to open the quick-add popover programmatically.
///
/// MenuBarExtra handles click-to-open natively, but has no public API to open its popover
/// programmatically, so the hotkey opens this separate NSPopover. Both popovers host the same
/// `MenuBarView` and read from the shared `FocusCoreService`, so state stays consistent.
@MainActor
public final class PopoverCoordinator: NSObject, ObservableObject {
    public static let shared = PopoverCoordinator()
    public let popover = NSPopover()

    private override init() {
        super.init()
        popover.behavior = .transient
        popover.animates = true
        popover.contentViewController = NSHostingController(
            rootView: MenuBarView()
                .environmentObject(FocusCoreService.shared)
                .environmentObject(self)
        )
    }

    public func openQuickAdd() {
        guard !popover.isShown else { return }
        anchorAndShow()
    }

    public func close() {
        popover.performClose(nil)
    }

    private func anchorAndShow() {
        // Anchor near the menu bar (top of the main screen). Ideally this would be anchored
        // to the menu bar status item button; MenuBarExtra does not expose that button, so we
        // position at the top-center of the screen's visible frame as a fallback.
        guard let screen = NSScreen.main else { return }
        let barHeight: CGFloat = 26
        let rect = NSRect(x: screen.visibleFrame.midX - 160,
                          y: screen.visibleFrame.maxY - barHeight,
                          width: 320, height: 1)
        let anchor = NSView(frame: rect)
        popover.show(relativeTo: rect, of: anchor, preferredEdge: .minY)
    }
}