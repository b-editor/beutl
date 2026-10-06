import Foundation
import XCTest
@testable import BeutlAVF

final class ReaderFailureTests: XCTestCase {
    func testNativeErrorDescriptionIncludesDomainAndCode() {
        let error = NSError(
            domain: "AVFoundationErrorDomain", code: -11828,
            userInfo: [NSLocalizedDescriptionKey: "Cannot Open"])

        let message = describeNativeError(error)

        XCTAssertTrue(message.contains("Cannot Open"), message)
        XCTAssertTrue(message.contains("domain=AVFoundationErrorDomain, code=-11828"), message)
        XCTAssertFalse(message.contains("underlying:"), message)
    }

    func testNativeErrorDescriptionPreservesUnderlyingCauses() {
        let underlying = NSError(
            domain: NSOSStatusErrorDomain, code: -12848,
            userInfo: [NSLocalizedDescriptionKey: "Missing media data"])
        let error = NSError(
            domain: "AVFoundationErrorDomain", code: -11828,
            userInfo: [
                NSLocalizedDescriptionKey: "Cannot Open",
                NSUnderlyingErrorKey: underlying,
            ])

        let message = describeNativeError(error)

        XCTAssertTrue(message.contains("domain=AVFoundationErrorDomain, code=-11828"), message)
        XCTAssertTrue(message.contains("underlying: Missing media data"), message)
        XCTAssertTrue(message.contains("domain=\(NSOSStatusErrorDomain), code=-12848"), message)
    }

    func testIncompleteMP4ReportsNativeErrorDetails() throws {
        guard #available(macOS 13, *) else {
            throw XCTSkip("Async track loading requires macOS 13 or later.")
        }

        let url = FileManager.default.temporaryDirectory.appendingPathComponent(
            ".Project6.beutl-part-\(UUID().uuidString).mp4")
        try Data().write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        var handle: OpaquePointer?
        let result = url.path.withCString { path in
            beutl_avf_reader_open(path, 1, nil, &handle)
        }
        defer { beutl_avf_reader_close(handle) }

        XCTAssertEqual(result, -103)
        XCTAssertNil(handle)
        let message = getLastErrorMessage()
        XCTAssertTrue(message.hasPrefix("Reader failed: loadTracks failed:"), message)
        XCTAssertTrue(message.contains("domain=AVFoundationErrorDomain"), message)
        XCTAssertTrue(message.contains("code="), message)
    }
}
