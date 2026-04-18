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

    // Measures the dominant hot path (FullHD swizzle). Uses raw buffers so the benchmark
    // isolates the permute cost — no CVPixelBuffer lock/unlock overhead. The output is
    // purely informational: XCTest records the average and stddev so regressions show up
    // as a measurable slowdown without failing the test run.
    func testARGBToBGRA8888Performance() throws {
        let width = 1920
        let height = 1080
        let pixelCount = width * height
        var source = [UInt8](repeating: 0, count: pixelCount * 4)
        for i in 0..<pixelCount {
            let o = i * 4
            source[o + 0] = UInt8(i & 0xFF)
            source[o + 1] = UInt8((i >> 8) & 0xFF)
            source[o + 2] = UInt8((i >> 4) & 0xFF)
            source[o + 3] = 0xFF
        }

        let pb = try makeARGBPixelBuffer(width: width, height: height) { row, base, rowBytes in
            let srcRow = row * width * 4
            memcpy(base, source.withUnsafeBytes { $0.baseAddress! + srcRow }, width * 4)
            _ = rowBytes
        }

        let destCapacity = pixelCount * 4
        var destination = [UInt8](repeating: 0, count: destCapacity)

        measure {
            try? destination.withUnsafeMutableBytes { destPtr in
                try PixelConvert.copyToBGRA8888(
                    pixelBuffer: pb,
                    destBuffer: destPtr.baseAddress!,
                    destCapacityBytes: destCapacity,
                    destRowBytes: width * 4)
            }
        }
    }

    // HDR fast path: CV64RGBALE source shares the Beutl Rgba16161616 layout exactly, so the
    // conversion has to be a bit-exact row-wise memcpy.
    func testRGBA16LERoundTripIsBitExact() throws {
        let width = 8
        let height = 4

        let attrs: [CFString: Any] = [
            kCVPixelBufferPixelFormatTypeKey: kCVPixelFormatType_64RGBALE,
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as CFDictionary,
        ]
        var pixelBuffer: CVPixelBuffer?
        let createStatus = CVPixelBufferCreate(
            kCFAllocatorDefault, width, height,
            kCVPixelFormatType_64RGBALE, attrs as CFDictionary, &pixelBuffer)
        XCTAssertEqual(createStatus, kCVReturnSuccess)
        guard let pb = pixelBuffer else { throw XCTSkip("cannot allocate CV64RGBALE buffer") }

        // Fill with a deterministic 16-bit pattern so every channel crosses the byte boundary.
        CVPixelBufferLockBaseAddress(pb, [])
        guard let base = CVPixelBufferGetBaseAddress(pb) else {
            CVPixelBufferUnlockBaseAddress(pb, [])
            throw XCTSkip("base address nil")
        }
        let rowBytes = CVPixelBufferGetBytesPerRow(pb)
        let words = base.assumingMemoryBound(to: UInt16.self)
        let pixelsPerRow = rowBytes / 2
        var expected = [UInt16](repeating: 0, count: width * 4 * height)
        for row in 0..<height {
            for col in 0..<width {
                let r = UInt16((row * 47 + col * 13) & 0xFFFF)
                let g = UInt16((row * 101 + col * 5) & 0xFFFF)
                let b = UInt16((row * 29 + col * 211) & 0xFFFF)
                let a = UInt16(0xFFFF)
                let wordOffset = row * pixelsPerRow + col * 4
                words[wordOffset + 0] = r
                words[wordOffset + 1] = g
                words[wordOffset + 2] = b
                words[wordOffset + 3] = a
                let outIdx = row * width * 4 + col * 4
                expected[outIdx + 0] = r
                expected[outIdx + 1] = g
                expected[outIdx + 2] = b
                expected[outIdx + 3] = a
            }
        }
        CVPixelBufferUnlockBaseAddress(pb, [])

        let destCapacity = width * 4 * height * 2
        var destination = [UInt8](repeating: 0, count: destCapacity)
        try destination.withUnsafeMutableBytes { destPtr in
            try PixelConvert.copyToRGBA16161616(
                pixelBuffer: pb,
                destBuffer: destPtr.baseAddress!,
                destCapacityBytes: destCapacity,
                destRowBytes: width * 8)
        }

        destination.withUnsafeBufferPointer { destRaw in
            destRaw.withMemoryRebound(to: UInt16.self) { destWords in
                for i in 0..<expected.count {
                    XCTAssertEqual(destWords[i], expected[i], "pixel word \(i) diverged")
                }
            }
        }
    }

    // Exercises the CV64ARGB → Rgba16161616 fallback path that kicks in when VideoToolbox
    // returns HDR frames in big-endian interleaved ARGB16 instead of the preferred
    // little-endian RGBA. Every channel must be byte-swapped before the ARGB→RGBA permute;
    // a regression where only one of four channels gets swapped would show up here.
    func testARGB16BEToRGBA16LEIsBitExact() throws {
        let width = 4
        let height = 2

        var pixelBuffer: CVPixelBuffer?
        let attrs: [CFString: Any] = [
            kCVPixelBufferPixelFormatTypeKey: kCVPixelFormatType_64ARGB,
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as CFDictionary,
        ]
        let createStatus = CVPixelBufferCreate(
            kCFAllocatorDefault, width, height,
            kCVPixelFormatType_64ARGB, attrs as CFDictionary, &pixelBuffer)
        XCTAssertEqual(createStatus, kCVReturnSuccess)
        guard let pb = pixelBuffer else { throw XCTSkip("cannot allocate CV64ARGB buffer") }

        CVPixelBufferLockBaseAddress(pb, [])
        defer { CVPixelBufferUnlockBaseAddress(pb, []) }
        let srcRowBytes = CVPixelBufferGetBytesPerRow(pb)
        guard let base = CVPixelBufferGetBaseAddress(pb) else {
            throw XCTSkip("CVPixelBufferGetBaseAddress returned nil")
        }

        // CV64ARGB stores channels big-endian in memory: A_hi A_lo R_hi R_lo G_hi G_lo B_hi B_lo.
        // Fill with distinct values per channel so permute / swap bugs can't be masked by
        // accidental symmetry.
        var expected = [UInt16](repeating: 0, count: width * 4 * height)
        for row in 0..<height {
            for col in 0..<width {
                let a: UInt16 = UInt16(0x1100 + row * 0x100 + col)
                let r: UInt16 = UInt16(0x2200 + row * 0x100 + col)
                let g: UInt16 = UInt16(0x3300 + row * 0x100 + col)
                let b: UInt16 = UInt16(0x4400 + row * 0x100 + col)

                let rowPtr = base.advanced(by: row * srcRowBytes)
                let pixelPtr = rowPtr.advanced(by: col * 8).assumingMemoryBound(to: UInt8.self)
                // Write big-endian 16-bit samples byte by byte.
                pixelPtr[0] = UInt8(a >> 8); pixelPtr[1] = UInt8(a & 0xFF)
                pixelPtr[2] = UInt8(r >> 8); pixelPtr[3] = UInt8(r & 0xFF)
                pixelPtr[4] = UInt8(g >> 8); pixelPtr[5] = UInt8(g & 0xFF)
                pixelPtr[6] = UInt8(b >> 8); pixelPtr[7] = UInt8(b & 0xFF)

                // Expected destination: R G B A as little-endian UInt16s.
                let outIdx = row * width * 4 + col * 4
                expected[outIdx + 0] = r
                expected[outIdx + 1] = g
                expected[outIdx + 2] = b
                expected[outIdx + 3] = a
            }
        }

        let destCapacity = width * 4 * height * 2
        var destination = [UInt8](repeating: 0, count: destCapacity)
        try destination.withUnsafeMutableBytes { destPtr in
            try PixelConvert.copyToRGBA16161616(
                pixelBuffer: pb,
                destBuffer: destPtr.baseAddress!,
                destCapacityBytes: destCapacity,
                destRowBytes: width * 8)
        }

        destination.withUnsafeBufferPointer { destRaw in
            destRaw.withMemoryRebound(to: UInt16.self) { destWords in
                for i in 0..<expected.count {
                    XCTAssertEqual(destWords[i], expected[i], "sample \(i) diverged")
                }
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
