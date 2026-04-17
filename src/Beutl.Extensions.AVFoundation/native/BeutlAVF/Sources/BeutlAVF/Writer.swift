import AVFoundation
import AudioToolbox
import CBeutlAVFTypes
import CoreMedia
import CoreVideo
import Foundation

// Writer state machine: create → start → append_video/append_audio* → finish → close.
// Close is idempotent; finishing is optional (close without finish discards the file).
final class Writer {
    private let writer: AVAssetWriter
    private var videoInput: AVAssetWriterInput?
    private var videoAdaptor: AVAssetWriterInputPixelBufferAdaptor?
    private var audioInput: AVAssetWriterInput?
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

        let pixelBuffer = try PixelConvert.makeCVPixelBufferFromBGRA8888(
            source: bgra, width: width, height: height, srcRowBytes: rowBytes)

        while !adaptor.assetWriterInput.isReadyForMoreMediaData {
            // Block on the writer's queue rather than spinning hot.
            Thread.sleep(forTimeInterval: 0.001)
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

        while !input.isReadyForMoreMediaData {
            Thread.sleep(forTimeInterval: 0.001)
        }

        if !input.append(sb) {
            throw BeutlAVFError.writerFailed(
                writer.error?.localizedDescription ?? "AVAssetWriterInput.append returned false")
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
        let codec = Self.codecType(for: config.codec)
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

        var settings: [String: Any] = [
            AVVideoCodecKey: codec,
            AVVideoWidthKey: Int(config.width),
            AVVideoHeightKey: Int(config.height),
        ]
        if !compressionProps.isEmpty {
            settings[AVVideoCompressionPropertiesKey] = compressionProps
        }

        let input = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        input.expectsMediaDataInRealTime = true

        let sourceWidth = config.sourceWidth > 0 ? Int(config.sourceWidth) : Int(config.width)
        let sourceHeight = config.sourceHeight > 0 ? Int(config.sourceHeight) : Int(config.height)
        let pbAttrs: [String: Any] = [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
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
    private static func codecType(for raw: Int32) -> AVVideoCodecType {
        switch raw {
        case 2: return .jpeg
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
