import XCTest
@testable import BeutlAVF

final class VersionTests: XCTestCase {
    func testVersionIsPositive() {
        XCTAssertGreaterThan(beutl_avf_version(), 0)
    }
}
