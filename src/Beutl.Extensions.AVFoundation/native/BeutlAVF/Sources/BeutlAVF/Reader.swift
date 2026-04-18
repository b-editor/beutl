import AVFoundation
import CBeutlAVFTypes
import CoreMedia
import CoreVideo
import Foundation

private let kBeutlReaderModeVideo: Int32 = 1 << 0
private let kBeutlReaderModeAudio: Int32 = 1 << 1

struct RationalInt32 {
    var num: Int32
    var den: Int32
}

struct RationalInt64 {
    var num: Int64
    var den: Int64
}

// Approximate a Double with a 32-bit rational (suited for frame rates).
func rational32(from value: Double) -> RationalInt32 {
    guard value.isFinite, value > 0 else { return RationalInt32(num: 0, den: 1) }
    let candidates: [Int32] = [1001, 1000, 100, 60, 30, 25, 24, 1]
    for den in candidates {
        let numDouble = value * Double(den)
        let numRounded = numDouble.rounded()
        if abs(numDouble - numRounded) < 1e-4, numRounded >= 1, numRounded <= Double(Int32.max) {
            return RationalInt32(num: Int32(numRounded), den: den)
        }
    }
    let num = Int32(clamping: Int(value * 1000.0))
    return RationalInt32(num: num, den: 1000)
}

// Express a Double as a 64-bit rational with microsecond precision (used for durations).
func rational64(from value: Double) -> RationalInt64 {
    guard value.isFinite, value > 0 else { return RationalInt64(num: 0, den: 1) }
    let num = Int64((value * 1_000_000).rounded())
    return RationalInt64(num: num, den: 1_000_000)
}

// Top-level wrapper exposed via opaque handle to C#.
final class Reader {
    private let asset: AVURLAsset
    private let options: BeutlReaderOptions

    var videoContext: VideoReaderContext?
    var audioContext: AudioReaderContext?

    init(path: String, modeFlags: Int32, options: BeutlReaderOptions) throws {
        self.options = options

        let url = URL(fileURLWithPath: path)
        guard FileManager.default.fileExists(atPath: url.path) else {
            throw BeutlAVFError.fileNotFound(path)
        }

        self.asset = AVURLAsset(url: url)

        let wantsVideo = (modeFlags & kBeutlReaderModeVideo) != 0
        let wantsAudio = (modeFlags & kBeutlReaderModeAudio) != 0

        if wantsVideo {
            if let track = asset.tracks(withMediaType: .video).first {
                self.videoContext = try VideoReaderContext(asset: asset, track: track, options: options)
            }
        }

        if wantsAudio {
            if let track = asset.tracks(withMediaType: .audio).first {
                self.audioContext = try AudioReaderContext(asset: asset, track: track, options: options)
            }
        }
    }
}

final class VideoReaderContext {
    private let asset: AVURLAsset
    private let track: AVAssetTrack
    private let cache: VideoSampleCache
    private let thresholdFrameCount: Int
    private let pixelFormat: OSType
    private let isHdr: Bool

    private var reader: AVAssetReader
    private var output: AVAssetReaderTrackOutput
    private var currentTimestamp: CMTime = .zero

    let info: BeutlVideoInfo
    let frameRate: Double

    init(asset: AVURLAsset, track: AVAssetTrack, options: BeutlReaderOptions) throws {
        self.asset = asset
        self.track = track
        self.cache = VideoSampleCache(capacity: Int(options.maxVideoBufferSize))
        self.thresholdFrameCount = Int(options.thresholdFrameCount)

        let tags = ColorSpaceMapper.extract(from: track)
        self.isHdr = tags.isHdr
        // Request a wide output format for HDR content so we don't tone-map down to 8bpc
        // in the reader. CV32ARGB is kept for SDR — it matches Beutl's Bgra8888 after a
        // single byte-reverse and is what VideoToolbox produces cheaply.
        self.pixelFormat = tags.isHdr ? kCVPixelFormatType_64RGBALE : kCVPixelFormatType_32ARGB

        let (reader, output) = try Self.makeReaderAndOutput(
            asset: asset, track: track, start: nil, pixelFormat: self.pixelFormat)
        self.reader = reader
        self.output = output

        let dimensions = track.naturalSize
        let nominalFrameRate = track.nominalFrameRate
        self.frameRate = Double(nominalFrameRate)

        let duration = track.timeRange.duration
        let frameRational = rational32(from: Double(nominalFrameRate))
        let durationSeconds = CMTimeGetSeconds(duration)
        let durationRational = rational64(from: durationSeconds.isFinite ? durationSeconds : 0)
        let nominalFrameCount = Int64((durationSeconds * Double(nominalFrameRate)).rounded())

        var codecFourCC: Int32 = 0
        if let desc = track.formatDescriptions.first {
            let handle = desc as! CMFormatDescription
            codecFourCC = Int32(bitPattern: CMFormatDescriptionGetMediaSubType(handle))
        }

        self.info = BeutlVideoInfo(
            width: Int32(dimensions.width),
            height: Int32(dimensions.height),
            codecFourCC: codecFourCC,
            frameRateNum: frameRational.num,
            frameRateDen: frameRational.den,
            durationNum: durationRational.num,
            durationDen: durationRational.den,
            nominalFrameCount: nominalFrameCount,
            isHdr: tags.isHdr ? 1 : 0,
            transferFunction: tags.transfer,
            colorPrimaries: tags.primaries,
            bytesPerPixel: tags.isHdr ? 8 : 4)
    }

    func readFrame(index: Int, outBuffer: UnsafeMutableRawPointer, capacityBytes: Int, rowBytes: Int) throws {
        if let cached = cache.search(frame: index) {
            let pixelBuffer = try Self.pixelBuffer(from: cached)
            try copyOut(pixelBuffer, outBuffer: outBuffer, capacityBytes: capacityBytes, rowBytes: rowBytes)
            return
        }

        var currentFrame = cache.lastFrameNumber()
        if currentFrame == -1 {
            currentFrame = CMTimeUtilities.frame(fromTimestamp: currentTimestamp, rate: frameRate)
        }

        if index < currentFrame || (currentFrame + thresholdFrameCount) < index {
            let dest = CMTimeUtilities.timestamp(fromFrame: index, rate: frameRate)
            try seek(to: dest)
        }

        while let sample = try readNextSample() {
            let lastFrame = cache.lastFrameNumber()
            if index <= lastFrame {
                let pixelBuffer = try Self.pixelBuffer(from: sample)
                try copyOut(pixelBuffer, outBuffer: outBuffer, capacityBytes: capacityBytes, rowBytes: rowBytes)
                return
            }
        }

        throw BeutlAVFError.endOfStream
    }

    private func copyOut(
        _ pixelBuffer: CVPixelBuffer,
        outBuffer: UnsafeMutableRawPointer,
        capacityBytes: Int,
        rowBytes: Int
    ) throws {
        if isHdr {
            try PixelConvert.copyToRGBA16161616(
                pixelBuffer: pixelBuffer, destBuffer: outBuffer,
                destCapacityBytes: capacityBytes, destRowBytes: rowBytes)
        } else {
            try PixelConvert.copyToBGRA8888(
                pixelBuffer: pixelBuffer, destBuffer: outBuffer,
                destCapacityBytes: capacityBytes, destRowBytes: rowBytes)
        }
    }

    private func seek(to timestamp: CMTime) throws {
        cache.reset()
        let (newReader, newOutput) = try Self.makeReaderAndOutput(
            asset: asset, track: track, start: timestamp, pixelFormat: pixelFormat)
        self.reader = newReader
        self.output = newOutput
    }

    private func readNextSample() throws -> CMSampleBuffer? {
        guard let sample = output.copyNextSampleBuffer() else {
            if reader.status == .failed {
                throw BeutlAVFError.readerFailed(reader.error?.localizedDescription ?? "unknown")
            }
            return nil
        }
        guard CMSampleBufferDataIsReady(sample), CMSampleBufferIsValid(sample) else {
            return try readNextSample()
        }

        let pts = CMSampleBufferGetPresentationTimeStamp(sample)
        currentTimestamp = pts
        let frame = CMTimeUtilities.frame(fromTimestamp: pts, rate: frameRate)
        cache.add(frame: frame, sample: sample)
        return sample
    }

    private static func makeReaderAndOutput(
        asset: AVURLAsset,
        track: AVAssetTrack,
        start: CMTime?,
        pixelFormat: OSType
    ) throws -> (AVAssetReader, AVAssetReaderTrackOutput) {
        let reader: AVAssetReader
        do {
            reader = try AVAssetReader(asset: asset)
        } catch {
            throw BeutlAVFError.readerFailed(error.localizedDescription)
        }

        if let start = start {
            reader.timeRange = CMTimeRange(start: start, duration: .positiveInfinity)
        }

        let settings: [String: Any] = [
            kCVPixelBufferPixelFormatTypeKey as String: pixelFormat,
        ]
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
        output.alwaysCopiesSampleData = false

        guard reader.canAdd(output) else {
            throw BeutlAVFError.readerFailed("Cannot add video output to AVAssetReader.")
        }
        reader.add(output)

        if !reader.startReading() {
            throw BeutlAVFError.readerFailed(reader.error?.localizedDescription ?? "startReading failed")
        }

        return (reader, output)
    }

    private static func pixelBuffer(from sample: CMSampleBuffer) throws -> CVPixelBuffer {
        guard let buffer = CMSampleBufferGetImageBuffer(sample) else {
            throw BeutlAVFError.readerFailed("CMSampleBufferGetImageBuffer returned nil.")
        }
        return buffer
    }
}

final class AudioReaderContext {
    // Output is always Stereo32BitFloat interleaved; matches Beutl's Pcm<Stereo32BitFloat>.
    static let outputChannels: Int = 2
    static let outputBitDepth: Int = 32
    static let outputBytesPerFrame: Int = 8  // 32-bit float × 2ch

    private let asset: AVURLAsset
    private let track: AVAssetTrack
    private let cache: AudioSampleCache
    private let thresholdSampleCount: Int

    private var reader: AVAssetReader
    private var output: AVAssetReaderTrackOutput
    private var currentAudioTimestamp: CMTime = .zero
    private var firstGapTimestamp: CMTime = .zero

    let info: BeutlAudioInfo

    init(asset: AVURLAsset, track: AVAssetTrack, options: BeutlReaderOptions) throws {
        self.asset = asset
        self.track = track
        self.cache = AudioSampleCache(capacity: Int(options.maxAudioBufferSize))
        self.thresholdSampleCount = Int(options.thresholdSampleCount)
        self.cache.reset(blockAlign: Self.outputBytesPerFrame)

        let sampleRate = Int(track.naturalTimeScale)
        let (reader, output) = try Self.makeReaderAndOutput(asset: asset, track: track, start: nil, sampleRate: sampleRate)
        self.reader = reader
        self.output = output

        var codecFourCC: Int32 = 0
        var channelCount: Int32 = Int32(Self.outputChannels)
        if let desc = track.formatDescriptions.first {
            let handle = desc as! CMFormatDescription
            codecFourCC = Int32(bitPattern: CMFormatDescriptionGetMediaSubType(handle))
            if let asbdPtr = CMAudioFormatDescriptionGetStreamBasicDescription(handle) {
                channelCount = Int32(asbdPtr.pointee.mChannelsPerFrame)
            }
        }

        let duration = track.timeRange.duration
        let durationSeconds = CMTimeGetSeconds(duration)
        let nominalSampleCount = Int64((durationSeconds * Double(sampleRate)).rounded())
        let durationRational = rational64(from: durationSeconds.isFinite ? durationSeconds : 0)

        self.info = BeutlAudioInfo(
            sampleRate: Int32(sampleRate),
            channelCount: channelCount,
            codecFourCC: codecFourCC,
            durationNum: durationRational.num,
            durationDen: durationRational.den,
            nominalSampleCount: nominalSampleCount)

        try calibrateFirstGap()
    }

    func readSamples(
        startSample: Int,
        length: Int,
        outBuffer: UnsafeMutableRawPointer,
        capacityBytes: Int
    ) throws {
        let requiredBytes = length * Self.outputBytesPerFrame
        guard capacityBytes >= requiredBytes else {
            throw BeutlAVFError.bufferTooSmall(required: requiredBytes, actual: capacityBytes)
        }

        // Zero-fill first so uncovered ranges read as silence instead of leaked memory.
        outBuffer.initializeMemory(as: UInt8.self, repeating: 0, count: requiredBytes)

        var cursor = startSample
        var remaining = length
        var buffer: UnsafeMutableRawPointer = outBuffer

        if cache.copyInto(startSample: &cursor, remaining: &remaining, buffer: &buffer) {
            return
        }

        var currentSample = cache.lastAudioSampleNumber()
        if currentSample == -1 {
            currentSample = Int(currentAudioTimestamp.value)
        }

        let sampleRate = Int(info.sampleRate)
        if cursor < currentSample || (currentSample + thresholdSampleCount) < cursor {
            let dest = CMTime(value: CMTimeValue(cursor), timescale: CMTimeScale(sampleRate))
            try seek(to: dest)
        }

        while let _ = try readNextSample() {
            if cache.copyInto(startSample: &cursor, remaining: &remaining, buffer: &buffer),
               remaining == 0 {
                return
            }
            if remaining == 0 { return }
        }
        // Reached EOS; trailing zeros in outBuffer remain as silence.
    }

    private func seek(to timestamp: CMTime) throws {
        cache.reset(blockAlign: Self.outputBytesPerFrame)
        let sampleRate = Int(info.sampleRate)
        let (newReader, newOutput) = try Self.makeReaderAndOutput(asset: asset, track: track, start: timestamp, sampleRate: sampleRate)
        self.reader = newReader
        self.output = newOutput
    }

    private func readNextSample() throws -> CMSampleBuffer? {
        guard let sample = output.copyNextSampleBuffer() else {
            if reader.status == .failed {
                throw BeutlAVFError.readerFailed(reader.error?.localizedDescription ?? "unknown")
            }
            return nil
        }
        guard CMSampleBufferDataIsReady(sample), CMSampleBufferIsValid(sample) else {
            return try readNextSample()
        }

        var timestamp = CMSampleBufferGetPresentationTimeStamp(sample)
        timestamp = CMTimeSubtract(timestamp, firstGapTimestamp)
        let startSample = Int(timestamp.value)
        cache.add(startSample: startSample, sample: sample)
        currentAudioTimestamp = timestamp
        return sample
    }

    private func calibrateFirstGap() throws {
        guard let _ = try readNextSample() else {
            throw BeutlAVFError.readerFailed("calibrateFirstGap: no samples available")
        }
        let firstAudioTimestamp = currentAudioTimestamp
        try seek(to: .zero)
        currentAudioTimestamp = .zero
        firstGapTimestamp = firstAudioTimestamp
    }

    private static func makeReaderAndOutput(
        asset: AVURLAsset,
        track: AVAssetTrack,
        start: CMTime?,
        sampleRate: Int
    ) throws -> (AVAssetReader, AVAssetReaderTrackOutput) {
        let reader: AVAssetReader
        do {
            reader = try AVAssetReader(asset: asset)
        } catch {
            throw BeutlAVFError.readerFailed(error.localizedDescription)
        }
        if let start = start {
            reader.timeRange = CMTimeRange(start: start, duration: .positiveInfinity)
        }

        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVLinearPCMBitDepthKey: outputBitDepth,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsFloatKey: true,
            AVLinearPCMIsNonInterleaved: false,
            AVSampleRateKey: sampleRate,
            AVNumberOfChannelsKey: outputChannels,
        ]
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
        output.alwaysCopiesSampleData = false

        guard reader.canAdd(output) else {
            throw BeutlAVFError.readerFailed("Cannot add audio output to AVAssetReader.")
        }
        reader.add(output)

        if !reader.startReading() {
            throw BeutlAVFError.readerFailed(reader.error?.localizedDescription ?? "startReading failed")
        }
        return (reader, output)
    }
}
