import XCTest
@testable import BeutlAVF

final class VersionTests: XCTestCase {
    func testVersionIncludesEncoderInputLayout() {
        XCTAssertEqual(beutl_avf_version(), 5)
    }
}
