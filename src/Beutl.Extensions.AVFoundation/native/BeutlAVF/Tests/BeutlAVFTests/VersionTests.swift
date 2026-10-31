import XCTest
@testable import BeutlAVF

final class VersionTests: XCTestCase {
    func testVersionIncludesNativeAudioLayout() {
        XCTAssertEqual(beutl_avf_version(), 4)
    }
}
