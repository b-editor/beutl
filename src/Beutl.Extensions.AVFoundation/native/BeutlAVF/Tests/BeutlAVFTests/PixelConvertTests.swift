import XCTest
import CoreVideo
@testable import BeutlAVF

final class PixelConvertTests: XCTestCase {
    private func makeARGBPixelBuffer(
        width: Int,
        height: Int,
        populate: (_ row: Int, _ base: UnsafeMutablePointer<UInt8>, _ rowBytes: Int) -> Void
    ) throws -> CVPixelBuffer {
        let attrs: [CFString: Any] = [
            kCVPixelBufferPixelFormatTypeKey: kCVPixelFormatType_32ARGB,
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as CFDictionary,
        ]
        var pixelBuffer: CVPixelBuffer?
        let status = CVPixelBufferCreate(
            kCFAllocatorDefault, width, height,
            kCVPixelFormatType_32ARGB, attrs as CFDictionary, &pixelBuffer)
        XCTAssertEqual(status, kCVReturnSuccess)
        guard let pb = pixelBuffer else { throw XCTSkip("could not allocate CVPixelBuffer") }

        CVPixelBufferLockBaseAddress(pb, [])
        defer { CVPixelBufferUnlockBaseAddress(pb, []) }
        guard let base = CVPixelBufferGetBaseAddress(pb)?.assumingMemoryBound(to: UInt8.self) else {
            throw XCTSkip("base address is nil")
        }
        let rowBytes = CVPixelBufferGetBytesPerRow(pb)
        for row in 0..<height {
            populate(row, base.advanced(by: row * rowBytes), rowBytes)
        }
        return pb
    }


    // Verify that a CV32ARGB-formatted input ([A][R][G][B] per pixel in memory) is
    // converted to the Bgra8888 layout ([B][G][R][A]) that Beutl's SKBitmap expects.
    // A small 2×1 buffer is used so IOSurface padding forces the row-based (padded)
    // swizzle path; the larger test below exercises the fast 32-bit word path.
    func testARGBToBGRA8888Padded() throws {
        let width = 2
        let height = 1
        let pb = try makeARGBPixelBuffer(width: width, height: height) { row, base, _ in
            if row == 0 {
                // Pixel 0: A=0x12 R=0x34 G=0x56 B=0x78
                base[0] = 0x12; base[1] = 0x34; base[2] = 0x56; base[3] = 0x78
                // Pixel 1: A=0xFF R=0x00 G=0xAA B=0x55
                base[4] = 0xFF; base[5] = 0x00; base[6] = 0xAA; base[7] = 0x55
            }
        }

        let destCapacity = width * height * 4
        var destination = [UInt8](repeating: 0, count: destCapacity)
        try destination.withUnsafeMutableBytes { destPtr in
            try PixelConvert.copyToBGRA8888(
                pixelBuffer: pb,
                destBuffer: destPtr.baseAddress!,
                destCapacityBytes: destCapacity,
                destRowBytes: width * 4)
        }

        XCTAssertEqual(destination, [
            0x78, 0x56, 0x34, 0x12,
            0x55, 0xAA, 0x00, 0xFF,
        ])
    }

    // Construct a 64×4 ARGB buffer (a width whose bytes-per-row is almost always the
    // tight multiple IOSurface would pick) so the fast path is exercised.
    func testARGBToBGRA8888FastPath() throws {
        let width = 64
        let height = 4
        let pb = try makeARGBPixelBuffer(width: width, height: height) { row, base, rowBytes in
            for col in 0..<width {
                let o = col * 4
                base[o + 0] = UInt8(row * 16 + col)    // A
                base[o + 1] = UInt8((col * 2) & 0xFF)  // R
                base[o + 2] = UInt8((col * 3) & 0xFF)  // G
                base[o + 3] = UInt8((col * 5) & 0xFF)  // B
            }
            _ = rowBytes  // padded bytes stay untouched; we only populate width*4
        }

        let dstRowBytes = width * 4
        let destCapacity = dstRowBytes * height
        var destination = [UInt8](repeating: 0, count: destCapacity)
        try destination.withUnsafeMutableBytes { destPtr in
            try PixelConvert.copyToBGRA8888(
                pixelBuffer: pb,
                destBuffer: destPtr.baseAddress!,
                destCapacityBytes: destCapacity,
                destRowBytes: dstRowBytes)
        }

        for row in 0..<height {
            for col in 0..<width {
                let d = row * dstRowBytes + col * 4
                XCTAssertEqual(destination[d + 0], UInt8((col * 5) & 0xFF), "B at (\(col),\(row))")
                XCTAssertEqual(destination[d + 1], UInt8((col * 3) & 0xFF), "G at (\(col),\(row))")
                XCTAssertEqual(destination[d + 2], UInt8((col * 2) & 0xFF), "R at (\(col),\(row))")
                XCTAssertEqual(destination[d + 3], UInt8(row * 16 + col), "A at (\(col),\(row))")
            }
        }
    }

    func testBGRA8888RoundTripThroughBufferCreate() throws {
        // Build a CV32BGRA pixel buffer from a known BGRA source, then round-trip.
        let width = 4
        let height = 4
        let stride = width * 4
        var source = [UInt8](repeating: 0, count: stride * height)
        for y in 0..<height {
            for x in 0..<width {
                let idx = y * stride + x * 4
                source[idx + 0] = UInt8(x * 16)   // B
                source[idx + 1] = UInt8(y * 16)   // G
                source[idx + 2] = UInt8(128)      // R
                source[idx + 3] = 0xFF            // A
            }
        }

        let pixelBuffer = try source.withUnsafeBytes { srcPtr in
            try PixelConvert.makeCVPixelBufferFromBGRA8888(
                source: srcPtr.baseAddress!,
                width: width, height: height, srcRowBytes: stride)
        }

        CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly) }

        let pbRowBytes = CVPixelBufferGetBytesPerRow(pixelBuffer)
        guard let base = CVPixelBufferGetBaseAddress(pixelBuffer)?.assumingMemoryBound(to: UInt8.self) else {
            XCTFail("pixel buffer base address nil"); return
        }
        for y in 0..<height {
            for x in 0..<width {
                let srcIdx = y * stride + x * 4
                let dstIdx = y * pbRowBytes + x * 4
                XCTAssertEqual(base[dstIdx + 0], source[srcIdx + 0], "B at (\(x),\(y))")
                XCTAssertEqual(base[dstIdx + 1], source[srcIdx + 1], "G at (\(x),\(y))")
                XCTAssertEqual(base[dstIdx + 2], source[srcIdx + 2], "R at (\(x),\(y))")
                XCTAssertEqual(base[dstIdx + 3], source[srcIdx + 3], "A at (\(x),\(y))")
            }
        }
    }
}
