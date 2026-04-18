import Accelerate
import CoreImage
import CoreMedia
import CoreVideo
import Foundation

enum PixelConvert {
    // Copy a CVPixelBuffer produced by AVAssetReader (CV32ARGB) into a caller-supplied
    // BGRA8888 buffer with the layout expected by Beutl's SkiaSharp Bitmap.
    // `destRowBytes` is expected to equal `width * 4` (Bitmap always uses tight packing).
    static func copyToBGRA8888(
        pixelBuffer: CVPixelBuffer,
        destBuffer: UnsafeMutableRawPointer,
        destCapacityBytes: Int,
        destRowBytes: Int
    ) throws {
        let width = CVPixelBufferGetWidth(pixelBuffer)
        let height = CVPixelBufferGetHeight(pixelBuffer)
        let requiredBytes = destRowBytes * height

        guard destCapacityBytes >= requiredBytes else {
            throw BeutlAVFError.bufferTooSmall(required: requiredBytes, actual: destCapacityBytes)
        }

        let lockResult = CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        guard lockResult == kCVReturnSuccess else {
            throw BeutlAVFError.readerFailed("CVPixelBufferLockBaseAddress failed: \(lockResult)")
        }
        defer { CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly) }

        let srcRowBytes = CVPixelBufferGetBytesPerRow(pixelBuffer)
        let srcPtr = CVPixelBufferGetBaseAddress(pixelBuffer)
        let pixelFormat = CVPixelBufferGetPixelFormatType(pixelBuffer)

        // CV32ARGB → Bgra8888 is a full byte reverse per pixel. vImage's
        // vImagePermuteChannels_ARGB8888 does this with SIMD + multithreading and handles
        // padded rows natively, so the same call covers both the tight and padded paths.
        if let srcPtr = srcPtr, pixelFormat == kCVPixelFormatType_32ARGB {
            swizzleARGBtoBGRA(
                src: srcPtr, dst: destBuffer,
                width: width, height: height,
                srcRowBytes: srcRowBytes, dstRowBytes: destRowBytes)
            return
        }

        // Only non-ARGB sources (e.g. YUV / HDR) fall back to CoreImage.
        try renderViaCoreImage(
            pixelBuffer: pixelBuffer,
            destBuffer: destBuffer,
            width: width, height: height,
            destRowBytes: destRowBytes)
    }

    // Copy a CVPixelBuffer carrying HDR content (16-bit per channel) into a caller-supplied
    // Rgba16161616 buffer. Accepts either kCVPixelFormatType_64RGBALE (native match — plain
    // row-wise memcpy) or kCVPixelFormatType_64ARGB (big-endian ARGB — permuted through
    // vImage to produce little-endian RGBA).
    static func copyToRGBA16161616(
        pixelBuffer: CVPixelBuffer,
        destBuffer: UnsafeMutableRawPointer,
        destCapacityBytes: Int,
        destRowBytes: Int
    ) throws {
        let width = CVPixelBufferGetWidth(pixelBuffer)
        let height = CVPixelBufferGetHeight(pixelBuffer)
        let requiredBytes = destRowBytes * height
        guard destCapacityBytes >= requiredBytes else {
            throw BeutlAVFError.bufferTooSmall(required: requiredBytes, actual: destCapacityBytes)
        }

        let lockResult = CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        guard lockResult == kCVReturnSuccess else {
            throw BeutlAVFError.readerFailed("CVPixelBufferLockBaseAddress failed: \(lockResult)")
        }
        defer { CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly) }

        let srcRowBytes = CVPixelBufferGetBytesPerRow(pixelBuffer)
        guard let srcPtr = CVPixelBufferGetBaseAddress(pixelBuffer) else {
            throw BeutlAVFError.readerFailed("CVPixelBufferGetBaseAddress returned nil.")
        }
        let format = CVPixelBufferGetPixelFormatType(pixelBuffer)
        let rowBytesToCopy = min(srcRowBytes, destRowBytes)

        switch format {
        case kCVPixelFormatType_64RGBALE:
            // Same layout as Bgra / Rgba16161616 in SkiaSharp: R, G, B, A, each 2-byte LE.
            for row in 0..<height {
                memcpy(destBuffer.advanced(by: row * destRowBytes),
                       srcPtr.advanced(by: row * srcRowBytes),
                       rowBytesToCopy)
            }
        case kCVPixelFormatType_64ARGB:
            // 16-bit integer ARGB, big-endian. We need little-endian RGBA with the channels
            // in R, G, B, A order. That's a 16-bit channel permute + endian swap, which
            // vImagePermuteChannels_ARGB16U + vImageByteSwap_Planar16U give us.
            try permuteARGB16BEtoRGBA16LE(
                src: srcPtr, srcRowBytes: srcRowBytes,
                dst: destBuffer, dstRowBytes: destRowBytes,
                width: width, height: height)
        default:
            // Unknown HDR layout: fall back to CoreImage with 16-bit integer RGBA output.
            try renderViaCoreImageRGBA16(
                pixelBuffer: pixelBuffer, destBuffer: destBuffer,
                width: width, height: height, destRowBytes: destRowBytes)
        }
    }

    // Copy a BGRA8888 source into an existing CVPixelBuffer (CV32BGRA), reusing a buffer
    // obtained from an AVAssetWriterInputPixelBufferAdaptor.pixelBufferPool when available.
    static func copyBGRAIntoPixelBuffer(
        source: UnsafeRawPointer,
        srcRowBytes: Int,
        into pixelBuffer: CVPixelBuffer
    ) throws {
        let lockResult = CVPixelBufferLockBaseAddress(pixelBuffer, [])
        guard lockResult == kCVReturnSuccess else {
            throw BeutlAVFError.writerFailed("CVPixelBufferLockBaseAddress failed: \(lockResult)")
        }
        defer { CVPixelBufferUnlockBaseAddress(pixelBuffer, []) }

        let height = CVPixelBufferGetHeight(pixelBuffer)
        let dstRowBytes = CVPixelBufferGetBytesPerRow(pixelBuffer)
        guard let dstBase = CVPixelBufferGetBaseAddress(pixelBuffer) else {
            throw BeutlAVFError.writerFailed("CVPixelBufferGetBaseAddress returned nil")
        }

        if srcRowBytes == dstRowBytes {
            memcpy(dstBase, source, dstRowBytes * height)
            return
        }

        let copyBytes = min(srcRowBytes, dstRowBytes)
        for row in 0..<height {
            memcpy(dstBase.advanced(by: row * dstRowBytes),
                   source.advanced(by: row * srcRowBytes),
                   copyBytes)
        }
    }

    // Fallback path for the first few frames (before the adaptor materializes its pool)
    // or when no pool is offered: allocate a fresh CV32BGRA pixel buffer and copy into it.
    static func makeCVPixelBufferFromBGRA8888(
        source: UnsafeRawPointer,
        width: Int,
        height: Int,
        srcRowBytes: Int
    ) throws -> CVPixelBuffer {
        return try makeCVPixelBuffer(
            pixelFormat: kCVPixelFormatType_32BGRA,
            source: source, width: width, height: height, srcRowBytes: srcRowBytes)
    }

    // HDR fallback variant matching the CV64RGBALE adaptor format.
    static func makeCVPixelBufferRGBA16LE(
        source: UnsafeRawPointer,
        width: Int,
        height: Int,
        srcRowBytes: Int
    ) throws -> CVPixelBuffer {
        return try makeCVPixelBuffer(
            pixelFormat: kCVPixelFormatType_64RGBALE,
            source: source, width: width, height: height, srcRowBytes: srcRowBytes)
    }

    private static func makeCVPixelBuffer(
        pixelFormat: OSType,
        source: UnsafeRawPointer,
        width: Int,
        height: Int,
        srcRowBytes: Int
    ) throws -> CVPixelBuffer {
        let attrs: [CFString: Any] = [
            kCVPixelBufferPixelFormatTypeKey: pixelFormat,
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferCGImageCompatibilityKey: true,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as CFDictionary,
        ]

        var pixelBuffer: CVPixelBuffer?
        let status = CVPixelBufferCreate(
            kCFAllocatorDefault, width, height,
            pixelFormat, attrs as CFDictionary, &pixelBuffer)
        guard status == kCVReturnSuccess, let pb = pixelBuffer else {
            throw BeutlAVFError.writerFailed("CVPixelBufferCreate failed: \(status)")
        }

        try copyBGRAIntoPixelBuffer(source: source, srcRowBytes: srcRowBytes, into: pb)
        return pb
    }

    private static func swizzleARGBtoBGRA(
        src: UnsafeRawPointer,
        dst: UnsafeMutableRawPointer,
        width: Int,
        height: Int,
        srcRowBytes: Int,
        dstRowBytes: Int
    ) {
        var srcBuffer = vImage_Buffer(
            data: UnsafeMutableRawPointer(mutating: src),
            height: vImagePixelCount(height),
            width: vImagePixelCount(width),
            rowBytes: srcRowBytes)
        var dstBuffer = vImage_Buffer(
            data: dst,
            height: vImagePixelCount(height),
            width: vImagePixelCount(width),
            rowBytes: dstRowBytes)

        // Permute map: destination byte i receives source byte permuteMap[i].
        // Source ARGB memory order [A=0, R=1, G=2, B=3] → destination BGRA [B, G, R, A].
        var permuteMap: [UInt8] = [3, 2, 1, 0]
        let err = vImagePermuteChannels_ARGB8888(
            &srcBuffer, &dstBuffer, &permuteMap, vImage_Flags(kvImageNoFlags))
        if err == kvImageNoError { return }

        // Accelerate is always available on macOS 12+; this fallback exists so an unexpected
        // failure surfaces as corrupted pixels rather than silently producing garbage.
        setLastErrorMessage("vImagePermuteChannels_ARGB8888 failed: \(err); falling back to scalar swizzle")
        scalarSwizzleARGBtoBGRA(src: src, dst: dst, width: width, height: height,
                                srcRowBytes: srcRowBytes, dstRowBytes: dstRowBytes)
    }

    private static func scalarSwizzleARGBtoBGRA(
        src: UnsafeRawPointer,
        dst: UnsafeMutableRawPointer,
        width: Int,
        height: Int,
        srcRowBytes: Int,
        dstRowBytes: Int
    ) {
        DispatchQueue.concurrentPerform(iterations: height) { row in
            let srcRow = src.advanced(by: row * srcRowBytes).assumingMemoryBound(to: UInt8.self)
            let dstRow = dst.advanced(by: row * dstRowBytes).assumingMemoryBound(to: UInt8.self)
            for col in 0..<width {
                let s = srcRow.advanced(by: col * 4)
                let d = dstRow.advanced(by: col * 4)
                let a = s[0]
                let r = s[1]
                let g = s[2]
                let b = s[3]
                d[0] = b
                d[1] = g
                d[2] = r
                d[3] = a
            }
        }
    }

    private static func permuteARGB16BEtoRGBA16LE(
        src: UnsafeRawPointer,
        srcRowBytes: Int,
        dst: UnsafeMutableRawPointer,
        dstRowBytes: Int,
        width: Int,
        height: Int
    ) throws {
        // vImageByteSwap_Planar16U treats the buffer as a single-channel 16-bit plane, so
        // to byte-swap every channel of an interleaved ARGB16 image we must advertise the
        // row width in *samples* (pixels × 4 channels), not pixels. Using the pixel width
        // here would leave three-quarters of each row endian-flipped.
        var srcPlanar = vImage_Buffer(
            data: UnsafeMutableRawPointer(mutating: src),
            height: vImagePixelCount(height),
            width: vImagePixelCount(width * 4),
            rowBytes: srcRowBytes)
        var dstPlanar = vImage_Buffer(
            data: dst,
            height: vImagePixelCount(height),
            width: vImagePixelCount(width * 4),
            rowBytes: dstRowBytes)

        // Source samples are 16-bit big-endian; AVFoundation stores CV64ARGB in network order.
        let err = vImageByteSwap_Planar16U(&srcPlanar, &dstPlanar, vImage_Flags(kvImageNoFlags))
        if err != kvImageNoError {
            throw BeutlAVFError.readerFailed("vImageByteSwap_Planar16U failed: \(err)")
        }

        // After byte-swap `dst` holds A R G B channels in little-endian UInt16s. Permute in
        // place to R G B A to match Beutl's Rgba16161616; this call needs the pixel width.
        var dstPacked = vImage_Buffer(
            data: dst,
            height: vImagePixelCount(height),
            width: vImagePixelCount(width),
            rowBytes: dstRowBytes)
        var permuteMap: [UInt8] = [1, 2, 3, 0]
        let err2 = vImagePermuteChannels_ARGB16U(
            &dstPacked, &dstPacked, &permuteMap, vImage_Flags(kvImageNoFlags))
        if err2 != kvImageNoError {
            throw BeutlAVFError.readerFailed("vImagePermuteChannels_ARGB16U failed: \(err2)")
        }
    }

    private static func renderViaCoreImageRGBA16(
        pixelBuffer: CVPixelBuffer,
        destBuffer: UnsafeMutableRawPointer,
        width: Int,
        height: Int,
        destRowBytes: Int
    ) throws {
        let ciImage = CIImage(cvPixelBuffer: pixelBuffer)
        // Use an extended-sRGB linear working space so HDR code-values pass through as
        // scene-linear extended values — callers convert to their final display space
        // later via BitmapColorSpace.
        let colorSpace = CGColorSpace(name: CGColorSpace.extendedLinearSRGB) ?? CGColorSpaceCreateDeviceRGB()
        sharedCIContext.render(
            ciImage,
            toBitmap: destBuffer,
            rowBytes: destRowBytes,
            bounds: CGRect(x: 0, y: 0, width: width, height: height),
            format: .RGBA16,
            colorSpace: colorSpace)
    }

    private static var sharedCIContext: CIContext = CIContext(options: nil)

    private static func renderViaCoreImage(
        pixelBuffer: CVPixelBuffer,
        destBuffer: UnsafeMutableRawPointer,
        width: Int,
        height: Int,
        destRowBytes: Int
    ) throws {
        let ciImage = CIImage(cvPixelBuffer: pixelBuffer)
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        sharedCIContext.render(
            ciImage,
            toBitmap: destBuffer,
            rowBytes: destRowBytes,
            bounds: CGRect(x: 0, y: 0, width: width, height: height),
            format: .BGRA8,
            colorSpace: colorSpace)
    }
}
