// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "BeutlAVF",
    platforms: [.macOS(.v12)],
    products: [
        .library(
            name: "BeutlAVF",
            type: .dynamic,
            targets: ["BeutlAVF"]
        )
    ],
    targets: [
        .target(
            name: "CBeutlAVFTypes",
            path: "Sources/CBeutlAVFTypes",
            publicHeadersPath: "include"
        ),
        .target(
            name: "BeutlAVF",
            dependencies: ["CBeutlAVFTypes"],
            path: "Sources/BeutlAVF"
        ),
        .testTarget(
            name: "BeutlAVFTests",
            dependencies: ["BeutlAVF"],
            path: "Tests/BeutlAVFTests"
        )
    ]
)
