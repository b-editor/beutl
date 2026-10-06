import XCTest
@testable import BeutlAVF

final class VersionTests: XCTestCase {
    func testVersionIncludesAudioDecodedCount() {
        XCTAssertEqual(beutl_avf_version(), 2)
    }
}
