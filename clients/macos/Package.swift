// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "FocusCore",
    platforms: [.macOS(.v15)],
    products: [
        .executable(name: "FocusCore", targets: ["FocusCore"]),
    ],
    dependencies: [],
    targets: [
        .systemLibrary(
            name: "CFocusCore",
            path: "Frameworks",
            pkgConfig: nil,
            providers: []
        ),
        .executableTarget(
            name: "FocusCore",
            dependencies: ["CFocusCore"],
            path: "Sources/FocusCore",
            linkerSettings: [
                .linkedLibrary("focuscore"),
                .unsafeFlags(["-L", "Frameworks"])
            ]
        ),
    ]
)

// CI build notes:
//   * The Rust core builds `libfocuscore.a` (aarch64 + x86_64) via CI and exposes the C ABI
//     declared in FocusCoreService.swift (focuscore_open/close/quick_add/version).
//   * Link the static lib into this target on CI (linker flags or an `.systemLibrary`
//     target with a module map) before `swift build --release`.
//   * Artifact: .build/release/FocusCore