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

        // CV32ARGB → Bgra8888 swizzle covers the AVAssetReader output path. Padded source
        // rows are handled by the row-based loop inside swizzleARGBtoBGRA; IOSurface-backed
        // pixel buffers are almost always padded in practice.
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

    // Convert a BGRA8888 source into a newly-allocated CVPixelBuffer (CV32BGRA).
    // Returns a +1 retained pixel buffer; caller must release.
    static func makeCVPixelBufferFromBGRA8888(
        source: UnsafeRawPointer,
        width: Int,
        height: Int,
        srcRowBytes: Int
    ) throws -> CVPixelBuffer {
        let attrs: [CFString: Any] = [
            kCVPixelBufferPixelFormatTypeKey: kCVPixelFormatType_32BGRA,
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferCGImageCompatibilityKey: true,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as CFDictionary,
        ]

        var pixelBuffer: CVPixelBuffer?
        let status = CVPixelBufferCreate(
            kCFAllocatorDefault, width, height,
            kCVPixelFormatType_32BGRA, attrs as CFDictionary, &pixelBuffer)
        guard status == kCVReturnSuccess, let pb = pixelBuffer else {
            throw BeutlAVFError.writerFailed("CVPixelBufferCreate failed: \(status)")
        }

        let lockResult = CVPixelBufferLockBaseAddress(pb, [])
        guard lockResult == kCVReturnSuccess else {
            throw BeutlAVFError.writerFailed("CVPixelBufferLockBaseAddress failed: \(lockResult)")
        }
        defer { CVPixelBufferUnlockBaseAddress(pb, []) }

        guard let dstBase = CVPixelBufferGetBaseAddress(pb) else {
            throw BeutlAVFError.writerFailed("CVPixelBufferGetBaseAddress returned nil")
        }

        let dstRowBytes = CVPixelBufferGetBytesPerRow(pb)
        let copyBytes = min(srcRowBytes, dstRowBytes)
        for row in 0..<height {
            let src = source.advanced(by: row * srcRowBytes)
            let dst = dstBase.advanced(by: row * dstRowBytes)
            memcpy(dst, src, copyBytes)
        }

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
        let totalPixels = width * height
        let tightDst = (dstRowBytes == width * 4) && (srcRowBytes == width * 4)

        // CV32ARGB in memory is [A][R][G][B]; Bgra8888 is [B][G][R][A] — a full 4-byte reverse.
        if tightDst {
            let srcWords = src.assumingMemoryBound(to: UInt32.self)
            let dstWords = dst.assumingMemoryBound(to: UInt32.self)
            DispatchQueue.concurrentPerform(iterations: totalPixels) { i in
                dstWords[i] = srcWords[i].byteSwapped
            }
            return
        }

        // Padded rows (common for IOSurface-backed CVPixelBuffer output).
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
