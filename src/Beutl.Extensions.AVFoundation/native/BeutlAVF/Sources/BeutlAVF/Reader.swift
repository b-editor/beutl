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

    var videoContext: VideoReaderContext?
    var audioContext: AudioReaderContext?

    init(path: String, modeFlags: Int32, options: BeutlReaderOptions) throws {
        let url = URL(fileURLWithPath: path)
        guard FileManager.default.fileExists(atPath: url.path) else {
            throw BeutlAVFError.fileNotFound(path)
        }

        self.asset = AVURLAsset(url: url)

        let wantsVideo = (modeFlags & kBeutlReaderModeVideo) != 0
        let wantsAudio = (modeFlags & kBeutlReaderModeAudio) != 0

        if wantsVideo {
            if let track = try Self.loadTracks(asset: asset, mediaType: .video).first {
                self.videoContext = try VideoReaderContext(asset: asset, track: track, options: options)
            }
        }

        if wantsAudio {
            if let track = try Self.loadTracks(asset: asset, mediaType: .audio).first {
                self.audioContext = try AudioReaderContext(asset: asset, track: track, options: options)
            }
        }
    }

    // Bridge between the sync init shape and AVFoundation's newer async track loading.
    // On macOS 13+, `AVAsset.tracks(withMediaType:)` is deprecated in favor of the async
    // `loadTracks(withMediaType:)`. Block on the async call so callers keep a sync API.
    private static func loadTracks(asset: AVAsset, mediaType: AVMediaType) throws -> [AVAssetTrack] {
        if #available(macOS 13, *) {
            let semaphore = DispatchSemaphore(value: 0)
            var outcome: Result<[AVAssetTrack], Error> = .success([])
            Task.detached {
                do {
                    outcome = .success(try await asset.loadTracks(withMediaType: mediaType))
                } catch {
                    outcome = .failure(error)
                }
                semaphore.signal()
            }
            semaphore.wait()
            do {
                return try outcome.get()
            } catch {
                throw BeutlAVFError.readerFailed("loadTracks failed: \(describeNativeError(error))")
            }
        }
        return asset.tracks(withMediaType: mediaType)
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
    private var streamEnded = false
    // The number where the track ends; the last sample stays on screen until then.
    private let endFrame: Int

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
        let frameRational = rational32(from: Double(track.nominalFrameRate))
        // Samples are numbered at the rate the caller gets, since it turns times into numbers at that rate.
        self.frameRate = Double(frameRational.num) / Double(frameRational.den)
        self.endFrame = CMTimeUtilities.frame(fromTimestamp: CMTimeRangeGetEnd(track.timeRange), rate: frameRate)
        let nominalFrameCount = Int64(endFrame)

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
            nominalFrameCount: nominalFrameCount,
            isHdr: tags.isHdr ? 1 : 0,
            transferFunction: tags.transfer,
            colorPrimaries: tags.primaries)
    }

    func readFrame(index: Int, outBuffer: UnsafeMutableRawPointer, capacityBytes: Int, rowBytes: Int) throws {
        let pixelBuffer = try Self.pixelBuffer(from: try sampleOnScreen(at: index))
        try copyOut(pixelBuffer, outBuffer: outBuffer, capacityBytes: capacityBytes, rowBytes: rowBytes)
    }

    // A sample stays on screen from the number its timestamp rounds to until the next sample's number, so variable
    // frame rate media, which does not have one sample per number, shows the last sample numbered at or before
    // `index`. Numbers before the first sample show it.
    private func sampleOnScreen(at index: Int) throws -> CMSampleBuffer {
        if frameRate > 0 && index >= endFrame {
            throw BeutlAVFError.endOfStream
        }
        if let cached = cache.search(frame: index, streamEnded: streamEnded) {
            return cached
        }

        var currentFrame = cache.lastFrameNumber()
        if currentFrame == -1 {
            currentFrame = CMTimeUtilities.frame(fromTimestamp: currentTimestamp, rate: frameRate)
        }

        if index < currentFrame || (currentFrame + thresholdFrameCount) < index {
            // Start a few numbers early, as far as a forward read may decode, so that stepping backward finds the
            // frames before the target in the cache. The first sample reports the seek time if it started earlier,
            // so its number is at most the target too.
            let lead = max(0, min(cache.capacity - 2, thresholdFrameCount))
            try seek(to: CMTimeUtilities.timestamp(
                fromFrame: max(0, index - lead), rateNum: info.frameRateNum, rateDen: info.frameRateDen))
        }

        // Decode until a sample starts after the target; the one before it is on screen.
        while let sample = try readNextSample() {
            if index < cache.lastFrameNumber() {
                return cache.search(frame: index, streamEnded: false) ?? sample
            }
        }

        // The last sample stays on screen until the track ends.
        if let last = cache.search(frame: index, streamEnded: true) {
            return last
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
        streamEnded = false
        // A seek past the end decodes nothing, so the next read must not take its position from a frame before it.
        currentTimestamp = timestamp
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
            streamEnded = true
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
    static let outputBitDepth: Int = 32

    private let outputLayout: AudioOutputLayout
    private let outputBytesPerFrame: Int

    private let asset: AVURLAsset
    private let track: AVAssetTrack
    private let cache: AudioSampleCache
    private let thresholdSampleCount: Int
    private let endTimestamp: CMTime

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

        let formatDescription = track.formatDescriptions.first.map { $0 as! CMFormatDescription }
        let asbd = formatDescription.flatMap { CMAudioFormatDescriptionGetStreamBasicDescription($0)?.pointee }
        let codecFourCC = formatDescription.map { Int32(bitPattern: CMFormatDescriptionGetMediaSubType($0)) } ?? 0
        var layoutSize = 0
        let sourceLayout = formatDescription.flatMap { CMAudioFormatDescriptionGetChannelLayout($0, sizeOut: &layoutSize) }
        let outputLayout = AudioOutputLayout(
            channelCount: asbd.map { Int($0.mChannelsPerFrame) }, sourceLayout: sourceLayout, layoutSize: layoutSize)
        self.outputLayout = outputLayout
        self.outputBytesPerFrame = outputLayout.channelCount * MemoryLayout<Float>.size
        self.cache.reset(blockAlign: outputBytesPerFrame)

        // naturalTimeScale can be the movie timescale rather than the sample rate. Prefer the ASBD rate.
        let resolvedSampleRate = asbd?.mSampleRate ?? 0
        let sampleRate = resolvedSampleRate > 0
            ? Int(resolvedSampleRate.rounded())
            : Int(track.naturalTimeScale)
        let (reader, output) = try Self.makeReaderAndOutput(
            asset: asset, track: track, start: nil, sampleRate: sampleRate, layout: outputLayout)
        self.reader = reader
        self.output = output

        let timeRange = track.timeRange
        self.endTimestamp = CMTimeRangeGetEnd(timeRange)
        let duration = timeRange.duration
        let durationSeconds = CMTimeGetSeconds(duration)
        let durationRational = rational64(from: durationSeconds.isFinite ? durationSeconds : 0)

        self.info = BeutlAudioInfo(
            sampleRate: Int32(sampleRate),
            channelCount: Int32(outputLayout.channelCount),
            codecFourCC: codecFourCC,
            durationNum: durationRational.num,
            durationDen: durationRational.den,
            outputChannelCount: Int32(outputLayout.channelCount),
            speakerMask: outputLayout.speakerMask)

        try calibrateFirstGap()
    }

    func readSamples(
        startSample: Int,
        length: Int,
        outBuffer: UnsafeMutableRawPointer,
        capacityBytes: Int
    ) throws -> Int {
        let requiredBytes = length * outputBytesPerFrame
        guard capacityBytes >= requiredBytes else {
            throw BeutlAVFError.bufferTooSmall(required: requiredBytes, actual: capacityBytes)
        }

        // Zero-fill first so uncovered ranges read as silence instead of leaked memory.
        outBuffer.initializeMemory(as: UInt8.self, repeating: 0, count: requiredBytes)

        var cursor = startSample
        var remaining = length
        var buffer: UnsafeMutableRawPointer = outBuffer

        if cache.copyInto(startSample: &cursor, remaining: &remaining, buffer: &buffer) {
            return length
        }

        var currentSample = cache.lastAudioSampleNumber()
        if currentSample == -1 {
            currentSample = Int(currentAudioTimestamp.value)
        }

        let sampleRate = Int(info.sampleRate)
        if cursor < currentSample || (currentSample + thresholdSampleCount) < cursor {
            let streamTime = CMTime(value: CMTimeValue(cursor), timescale: CMTimeScale(sampleRate))
            // Undo PTS normalization for the absolute track end and AVAssetReader time range.
            let dest = CMTimeAdd(streamTime, firstGapTimestamp)
            // AVAssetReader can fail to start when its timeRange begins at/past the track end.
            // Treat that seek as EOF, preserving any frames already copied from the cache.
            if endTimestamp.isNumeric && CMTimeCompare(dest, endTimestamp) >= 0 {
                return length - remaining
            }
            try seek(to: dest)
        }

        while let _ = try readNextSample() {
            if cache.copyInto(startSample: &cursor, remaining: &remaining, buffer: &buffer) {
                return length
            }
        }
        // Report only covered frames; the zero-filled tail is not part of the decoded result.
        return length - remaining
    }

    private func seek(to timestamp: CMTime) throws {
        cache.reset(blockAlign: outputBytesPerFrame)
        let sampleRate = Int(info.sampleRate)
        let (newReader, newOutput) = try Self.makeReaderAndOutput(
            asset: asset, track: track, start: timestamp, sampleRate: sampleRate, layout: outputLayout)
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
        guard let sample = try readNextSample() else {
            throw BeutlAVFError.readerFailed("calibrateFirstGap: no samples available")
        }
        guard let format = CMSampleBufferGetFormatDescription(sample),
              let delivered = CMAudioFormatDescriptionGetStreamBasicDescription(format)?.pointee else {
            throw BeutlAVFError.readerFailed("Decoded audio has no stream description.")
        }
        guard delivered.mChannelsPerFrame == outputLayout.channelCount,
              delivered.mBytesPerFrame == outputBytesPerFrame,
              delivered.mFormatID == kAudioFormatLinearPCM,
              delivered.mBitsPerChannel == Self.outputBitDepth,
              delivered.mFormatFlags & kAudioFormatFlagIsFloat != 0,
              delivered.mFormatFlags & kAudioFormatFlagIsNonInterleaved == 0 else {
            throw BeutlAVFError.readerFailed(
                "Expected interleaved float32 audio with \(outputLayout.channelCount) channels and \(outputBytesPerFrame) bytes per frame; "
                + "received \(delivered.mChannelsPerFrame) channels, \(delivered.mBytesPerFrame) bytes per frame, "
                + "\(delivered.mBitsPerChannel) bits, format \(delivered.mFormatID), flags \(delivered.mFormatFlags).")
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
        sampleRate: Int,
        layout: AudioOutputLayout
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

        var settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVLinearPCMBitDepthKey: outputBitDepth,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsFloatKey: true,
            AVLinearPCMIsNonInterleaved: false,
            AVSampleRateKey: sampleRate,
            AVNumberOfChannelsKey: layout.channelCount,
        ]
        if let layoutData = layout.data {
            settings[AVChannelLayoutKey] = layoutData
        }
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
