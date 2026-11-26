// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "FocusCore",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "FocusCore", targets: ["FocusCore"]),
    ],
    dependencies: [],
    targets: [
        .executableTarget(
            name: "FocusCore",
            dependencies: [],
            path: "Sources"
        ),
    ]
)
