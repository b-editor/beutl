import AVFoundation
import AudioToolbox
import CBeutlAVFTypes
import CoreMedia
import CoreVideo
import Foundation
import VideoToolbox

// Writer state machine: create → start → append_video/append_audio* → finish → close.
// Close is idempotent; finishing is optional (close without finish discards the file).
final class Writer {
    private let writer: AVAssetWriter
    private var videoInput: AVAssetWriterInput?
    private var videoAdaptor: AVAssetWriterInputPixelBufferAdaptor?
    private var audioInput: AVAssetWriterInput?
    private var videoIsHdr: Bool = false
    private var started = false
    private var finished = false

    init(path: String, videoConfig: BeutlVideoEncoderConfig?, audioConfig: BeutlAudioEncoderConfig?) throws {
        let url = URL(fileURLWithPath: path)
        // Overwrite by default so repeated runs don't fail with "file exists".
        try? FileManager.default.removeItem(at: url)

        let writer: AVAssetWriter
        do {
            writer = try AVAssetWriter(outputURL: url, fileType: .mp4)
        } catch {
            throw BeutlAVFError.writerFailed(error.localizedDescription)
        }
        self.writer = writer

        if let videoConfig = videoConfig {
            try configureVideo(config: videoConfig)
        }
        if let audioConfig = audioConfig {
            try configureAudio(config: audioConfig)
        }
    }

    func start() throws {
        guard !started else { return }
        guard writer.startWriting() else {
            throw BeutlAVFError.writerFailed(writer.error?.localizedDescription ?? "startWriting failed")
        }
        writer.startSession(atSourceTime: .zero)
        started = true
    }

    // `rowBytes` disambiguates between SDR (width*4 — Bgra8888) and HDR (width*8 —
    // Rgba16161616) inputs. Callers on the C# side already carry this width in Bitmap.RowBytes.
    func appendVideo(
        bgra: UnsafeRawPointer,
        width: Int,
        height: Int,
        rowBytes: Int,
        ptsNum: Int64,
        ptsDen: Int32
    ) throws {
        guard started else { throw BeutlAVFError.writerFailed("Writer not started.") }
        guard let adaptor = videoAdaptor else { throw BeutlAVFError.writerFailed("Video input not configured.") }

        // The adaptor's pool becomes available after startSession; reusing buffers from it
        // avoids allocating a fresh CVPixelBuffer per frame (which for 4K is ~35 MB/frame
        // for BGRA and ~70 MB/frame for Rgba16161616).
        try waitUntilReady(input: adaptor.assetWriterInput)

        let pixelBuffer: CVPixelBuffer
        if let pool = adaptor.pixelBufferPool {
            var pb: CVPixelBuffer?
            let status = CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &pb)
            guard status == kCVReturnSuccess, let buffer = pb else {
                throw BeutlAVFError.writerFailed("CVPixelBufferPoolCreatePixelBuffer failed: \(status)")
            }
            // Both BGRA and RGBA16LE ultimately go in via row-wise memcpy; the helper
            // picks the right copy size based on the destination pixel buffer's bytes-per-row.
            try PixelConvert.copyBGRAIntoPixelBuffer(source: bgra, srcRowBytes: rowBytes, into: buffer)
            pixelBuffer = buffer
        } else if videoIsHdr {
            pixelBuffer = try PixelConvert.makeCVPixelBufferRGBA16LE(
                source: bgra, width: width, height: height, srcRowBytes: rowBytes)
        } else {
            pixelBuffer = try PixelConvert.makeCVPixelBufferFromBGRA8888(
                source: bgra, width: width, height: height, srcRowBytes: rowBytes)
        }

        let pts = CMTime(value: CMTimeValue(ptsNum), timescale: CMTimeScale(ptsDen))
        if !adaptor.append(pixelBuffer, withPresentationTime: pts) {
            throw BeutlAVFError.writerFailed(
                writer.error?.localizedDescription ?? "AVAssetWriterInputPixelBufferAdaptor.append returned false")
        }
    }

    func appendAudio(
        pcm: UnsafeMutableRawPointer,
        numSamples: Int,
        ptsSamples: Int64,
        sampleRate: Int
    ) throws {
        guard started else { throw BeutlAVFError.writerFailed("Writer not started.") }
        guard let input = audioInput else { throw BeutlAVFError.writerFailed("Audio input not configured.") }

        var asbd = AudioStreamBasicDescription(
            mSampleRate: Float64(sampleRate),
            mFormatID: kAudioFormatLinearPCM,
            mFormatFlags: kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
            mBytesPerPacket: 8,
            mFramesPerPacket: 1,
            mBytesPerFrame: 8,
            mChannelsPerFrame: 2,
            mBitsPerChannel: 32,
            mReserved: 0)

        var formatDesc: CMFormatDescription?
        let fmtStatus = CMAudioFormatDescriptionCreate(
            allocator: kCFAllocatorDefault,
            asbd: &asbd,
            layoutSize: 0, layout: nil,
            magicCookieSize: 0, magicCookie: nil,
            extensions: nil,
            formatDescriptionOut: &formatDesc)
        guard fmtStatus == noErr, let fmt = formatDesc else {
            throw BeutlAVFError.writerFailed("CMAudioFormatDescriptionCreate failed: \(fmtStatus)")
        }

        let dataLength = numSamples * 8  // 2ch × Float32
        var blockBuffer: CMBlockBuffer?
        let bbStatus = CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault,
            memoryBlock: pcm,
            blockLength: dataLength,
            blockAllocator: kCFAllocatorNull,
            customBlockSource: nil,
            offsetToData: 0,
            dataLength: dataLength,
            // Copy so the caller's buffer can be freed immediately after append returns.
            flags: kCMBlockBufferAlwaysCopyDataFlag,
            blockBufferOut: &blockBuffer)
        guard bbStatus == noErr, let bb = blockBuffer else {
            throw BeutlAVFError.writerFailed("CMBlockBufferCreateWithMemoryBlock failed: \(bbStatus)")
        }

        let pts = CMTime(value: CMTimeValue(ptsSamples), timescale: CMTimeScale(sampleRate))
        var sampleBuffer: CMSampleBuffer?
        let sbStatus = CMAudioSampleBufferCreateReadyWithPacketDescriptions(
            allocator: kCFAllocatorDefault,
            dataBuffer: bb,
            formatDescription: fmt,
            sampleCount: numSamples,
            presentationTimeStamp: pts,
            packetDescriptions: nil,
            sampleBufferOut: &sampleBuffer)
        guard sbStatus == noErr, let sb = sampleBuffer else {
            throw BeutlAVFError.writerFailed("CMAudioSampleBufferCreateReadyWithPacketDescriptions failed: \(sbStatus)")
        }

        try waitUntilReady(input: input)

        if !input.append(sb) {
            throw BeutlAVFError.writerFailed(
                writer.error?.localizedDescription ?? "AVAssetWriterInput.append returned false")
        }
    }

    // Wait until the input can accept more media data. If the writer transitions to
    // .failed or .cancelled while we're back-pressured, isReadyForMoreMediaData never flips
    // back to true, so we must surface that as an error instead of sleeping forever.
    // Uses exponential backoff (0.5ms → 10ms cap) so a 1ms hot loop doesn't peg a core
    // when the encoder applies sustained back-pressure during HDR/4K encodes.
    private func waitUntilReady(input: AVAssetWriterInput) throws {
        var sleepInterval: TimeInterval = 0.0005
        let maxSleepInterval: TimeInterval = 0.010
        while !input.isReadyForMoreMediaData {
            switch writer.status {
            case .failed:
                throw BeutlAVFError.writerFailed(
                    writer.error?.localizedDescription ?? "AVAssetWriter transitioned to .failed")
            case .cancelled:
                throw BeutlAVFError.writerFailed("AVAssetWriter was cancelled")
            case .completed:
                throw BeutlAVFError.writerFailed("AVAssetWriter already finished; cannot append more data")
            default:
                break
            }
            Thread.sleep(forTimeInterval: sleepInterval)
            sleepInterval = min(sleepInterval * 2, maxSleepInterval)
        }
    }

    func finish() throws {
        guard started, !finished else { return }
        videoInput?.markAsFinished()
        audioInput?.markAsFinished()

        let semaphore = DispatchSemaphore(value: 0)
        writer.finishWriting { semaphore.signal() }
        semaphore.wait()

        finished = true

        if writer.status == .failed {
            throw BeutlAVFError.writerFailed(writer.error?.localizedDescription ?? "finishWriting failed")
        }
    }

    private func configureVideo(config: BeutlVideoEncoderConfig) throws {
        let isHdr = config.isHdr != 0
        videoIsHdr = isHdr

        // HDR forces HEVC Main10 regardless of the requested codec slot; H.264 has no HDR10
        // profile in VideoToolbox and JPEG is moot for high-dynamic-range output.
        let codec: AVVideoCodecType = isHdr ? .hevc : Self.codecType(for: config.codec)

        var compressionProps: [String: Any] = [:]
        if config.bitrate > 0 {
            compressionProps[AVVideoAverageBitRateKey] = config.bitrate
        }
        if config.keyframeInterval > 0 {
            compressionProps[AVVideoMaxKeyFrameIntervalKey] = config.keyframeInterval
        }
        if codec == .jpeg, config.jpegQuality >= 0 {
            compressionProps[AVVideoQualityKey] = config.jpegQuality
        }
        if codec == .h264, let profile = Self.profileH264(for: config.profileLevelH264) {
            compressionProps[AVVideoProfileLevelKey] = profile
        }
        if isHdr {
            compressionProps[AVVideoProfileLevelKey] = kVTProfileLevel_HEVC_Main10_AutoLevel
        }

        var settings: [String: Any] = [
            AVVideoCodecKey: codec,
            AVVideoWidthKey: Int(config.width),
            AVVideoHeightKey: Int(config.height),
        ]
        if !compressionProps.isEmpty {
            settings[AVVideoCompressionPropertiesKey] = compressionProps
        }

        // Stamp the encoded stream with explicit color tags so HDR-aware players can pick the
        // right tone mapping. Uses whatever the caller requested; sensible defaults (Rec.2020
        // + PQ + Rec.2020 matrix) are applied when tags are left at Unknown in the HDR case.
        if let colorProps = Self.buildColorProperties(config: config, isHdr: isHdr) {
            settings[AVVideoColorPropertiesKey] = colorProps
        }

        let input = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        input.expectsMediaDataInRealTime = true

        let sourceWidth = config.sourceWidth > 0 ? Int(config.sourceWidth) : Int(config.width)
        let sourceHeight = config.sourceHeight > 0 ? Int(config.sourceHeight) : Int(config.height)
        // CV64RGBALE matches Beutl's Rgba16161616 byte layout on the C# side, so a 16-bit HDR
        // frame can land in the pool buffer via a plain row-wise memcpy.
        let pixelFormat: OSType = isHdr ? kCVPixelFormatType_64RGBALE : kCVPixelFormatType_32BGRA
        let pbAttrs: [String: Any] = [
            kCVPixelBufferPixelFormatTypeKey as String: pixelFormat,
            kCVPixelBufferWidthKey as String: sourceWidth,
            kCVPixelBufferHeightKey as String: sourceHeight,
        ]
        let adaptor = AVAssetWriterInputPixelBufferAdaptor(
            assetWriterInput: input,
            sourcePixelBufferAttributes: pbAttrs)

        guard writer.canAdd(input) else {
            throw BeutlAVFError.writerFailed("Cannot add video input to AVAssetWriter.")
        }
        writer.add(input)
        videoInput = input
        videoAdaptor = adaptor
    }

    private static func buildColorProperties(
        config: BeutlVideoEncoderConfig,
        isHdr: Bool
    ) -> [String: Any]? {
        let primariesKey = mapPrimaries(config.colorPrimaries, isHdr: isHdr)
        let transferKey = mapTransfer(config.colorTransfer, isHdr: isHdr)
        let matrixKey = mapMatrix(config.yCbCrMatrix, isHdr: isHdr)

        if primariesKey == nil && transferKey == nil && matrixKey == nil { return nil }

        var dict: [String: Any] = [:]
        if let v = primariesKey { dict[AVVideoColorPrimariesKey] = v }
        if let v = transferKey { dict[AVVideoTransferFunctionKey] = v }
        if let v = matrixKey { dict[AVVideoYCbCrMatrixKey] = v }
        return dict.isEmpty ? nil : dict
    }

    private static func mapPrimaries(_ raw: Int32, isHdr: Bool) -> String? {
        switch raw {
        case Int32(BEUTL_PRIMARIES_BT709): return AVVideoColorPrimaries_ITU_R_709_2
        case Int32(BEUTL_PRIMARIES_REC2020): return AVVideoColorPrimaries_ITU_R_2020
        case Int32(BEUTL_PRIMARIES_SMPTE431), Int32(BEUTL_PRIMARIES_DCIP3):
            return AVVideoColorPrimaries_P3_D65
        case Int32(BEUTL_PRIMARIES_EBU3213): return AVVideoColorPrimaries_EBU_3213
        case Int32(BEUTL_PRIMARIES_SMPTE170M): return AVVideoColorPrimaries_SMPTE_C
        default:
            // HDR without explicit tag → Rec.2020 gamut; SDR falls through to the encoder's
            // default (BT.709 for H.264/HEVC, unset for JPEG).
            return isHdr ? AVVideoColorPrimaries_ITU_R_2020 : nil
        }
    }

    private static func mapTransfer(_ raw: Int32, isHdr: Bool) -> String? {
        switch raw {
        case Int32(BEUTL_TRANSFER_PQ): return AVVideoTransferFunction_SMPTE_ST_2084_PQ
        case Int32(BEUTL_TRANSFER_HLG): return AVVideoTransferFunction_ITU_R_2100_HLG
        case Int32(BEUTL_TRANSFER_BT709): return AVVideoTransferFunction_ITU_R_709_2
        case Int32(BEUTL_TRANSFER_SMPTE240M): return AVVideoTransferFunction_SMPTE_240M_1995
        // AVVideoTransferFunction_Linear is macOS 13+. For BEUTL_TRANSFER_LINEAR we fall
        // through to the encoder default rather than gating the whole extension on 13+.
        default:
            return isHdr ? AVVideoTransferFunction_SMPTE_ST_2084_PQ : nil
        }
    }

    private static func mapMatrix(_ raw: Int32, isHdr: Bool) -> String? {
        switch raw {
        case Int32(BEUTL_MATRIX_BT709): return AVVideoYCbCrMatrix_ITU_R_709_2
        case Int32(BEUTL_MATRIX_BT601): return AVVideoYCbCrMatrix_ITU_R_601_4
        case Int32(BEUTL_MATRIX_REC2020): return AVVideoYCbCrMatrix_ITU_R_2020
        case Int32(BEUTL_MATRIX_SMPTE240M): return AVVideoYCbCrMatrix_SMPTE_240M_1995
        default:
            return isHdr ? AVVideoYCbCrMatrix_ITU_R_2020 : nil
        }
    }

    private func configureAudio(config: BeutlAudioEncoderConfig) throws {
        let formatId = UInt32(bitPattern: config.formatFourCC)
        var settings: [String: Any] = [
            AVFormatIDKey: formatId,
            AVSampleRateKey: Int(config.sampleRate),
            AVNumberOfChannelsKey: Int(config.channelCount),
        ]
        if config.bitrate > 0 {
            settings[AVEncoderBitRateKey] = config.bitrate
        }
        if config.quality >= 0 {
            settings[AVEncoderAudioQualityKey] = config.quality
        }
        if config.sampleRateConverterQuality >= 0 {
            settings[AVSampleRateConverterAudioQualityKey] = config.sampleRateConverterQuality
        }
        if formatId == kAudioFormatLinearPCM {
            settings[AVLinearPCMBitDepthKey] = Int(config.linearPcmBitDepth)
            settings[AVLinearPCMIsFloatKey] = (config.linearPcmFlags & 0x1) != 0
            settings[AVLinearPCMIsBigEndianKey] = (config.linearPcmFlags & 0x2) != 0
            settings[AVLinearPCMIsNonInterleaved] = (config.linearPcmFlags & 0x4) != 0
        }

        let input = AVAssetWriterInput(mediaType: .audio, outputSettings: settings)
        input.expectsMediaDataInRealTime = true

        guard writer.canAdd(input) else {
            throw BeutlAVFError.writerFailed("Cannot add audio input to AVAssetWriter.")
        }
        writer.add(input)
        audioInput = input
    }

    // Codec enum mapping: 0 (Default) falls back to H.264.
    // HDR paths force HEVC upstream regardless of this value.
    private static func codecType(for raw: Int32) -> AVVideoCodecType {
        switch raw {
        case 2: return .jpeg
        case 3: return .hevc
        case 1, 0: return .h264
        default: return .h264
        }
    }

    // Profile enum mapping mirrors AVFVideoEncoderSettings.VideoProfileLevelH264.
    private static func profileH264(for raw: Int32) -> String? {
        switch raw {
        case 1: return AVVideoProfileLevelH264Baseline30
        case 2: return AVVideoProfileLevelH264Baseline31
        case 3: return AVVideoProfileLevelH264Baseline41
        case 4: return AVVideoProfileLevelH264Main30
        case 5: return AVVideoProfileLevelH264Main31
        case 6: return AVVideoProfileLevelH264Main32
        case 7: return AVVideoProfileLevelH264Main41
        default: return nil
        }
    }
}
