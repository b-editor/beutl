import XCTest
@testable import BeutlAVF

final class ReadAudioInteropTests: XCTestCase {
    func testFailureClearsDecodedCount() {
        var decoded: Int32 = 123
        let result = beutl_avf_reader_read_audio(nil, 0, 1, nil, 0, &decoded)
        XCTAssertEqual(result, -1)
        XCTAssertEqual(decoded, 0)
    }

    func testDecodedCountPointerIsRequired() {
        let result = beutl_avf_reader_read_audio(nil, 0, 1, nil, 0, nil)
        XCTAssertEqual(result, -2)
    }
}
