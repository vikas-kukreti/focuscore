import Cocoa
import SwiftUI

@main
struct FocusCoreApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}

struct ContentView: View {
    var body: some View {
        VStack(spacing: 16) {
            Text("FocusCore").font(.largeTitle).bold()
            Text("Native task, timeboxing & Pomodoro suite.")
        }
        .frame(minWidth: 600, minHeight: 400)
    }
}
