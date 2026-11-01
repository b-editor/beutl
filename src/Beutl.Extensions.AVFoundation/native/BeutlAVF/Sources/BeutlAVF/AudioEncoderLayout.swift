import AudioToolbox
import Foundation

enum AudioEncoderLayout {
    static func inputTag(channelCount: Int, speakerMask: UInt32) -> AudioChannelLayoutTag {
        switch (channelCount, speakerMask) {
        case (1, 0x4): return kAudioChannelLayoutTag_Mono
        case (2, 0x3): return kAudioChannelLayoutTag_Stereo
        case (3, 0x7): return kAudioChannelLayoutTag_WAVE_3_0
        case (4, 0x33): return kAudioChannelLayoutTag_WAVE_4_0_A
        case (5, 0x37): return kAudioChannelLayoutTag_WAVE_5_0_A
        case (6, 0x3f): return kAudioChannelLayoutTag_MPEG_5_1_A
        case (7, 0x70f): return kAudioChannelLayoutTag_WAVE_6_1
        case (8, 0x63f): return kAudioChannelLayoutTag_WAVE_7_1
        default: return kAudioChannelLayoutTag_DiscreteInOrder | UInt32(channelCount)
        }
    }

    static func layout(_ tag: AudioChannelLayoutTag) -> AudioChannelLayout {
        var result = AudioChannelLayout()
        result.mChannelLayoutTag = tag
        return result
    }

    static func pcmFormat(sampleRate: Int, channelCount: Int) -> AudioStreamBasicDescription {
        AudioStreamBasicDescription(
            mSampleRate: Float64(sampleRate),
            mFormatID: kAudioFormatLinearPCM,
            mFormatFlags: kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
            mBytesPerPacket: UInt32(channelCount * MemoryLayout<Float>.size),
            mFramesPerPacket: 1,
            mBytesPerFrame: UInt32(channelCount * MemoryLayout<Float>.size),
            mChannelsPerFrame: UInt32(channelCount),
            mBitsPerChannel: 32,
            mReserved: 0)
    }

    static func availableEncodeTags(format: AudioFormatID, channelCount: Int) throws -> [AudioChannelLayoutTag] {
        var description = AudioStreamBasicDescription()
        description.mFormatID = format
        description.mChannelsPerFrame = UInt32(channelCount)
        let specifierSize = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        var size: UInt32 = 0
        try check(AudioFormatGetPropertyInfo(kAudioFormatProperty_AvailableEncodeChannelLayoutTags,
            specifierSize, &description, &size), "AvailableEncodeChannelLayoutTags size")
        var tags = [AudioChannelLayoutTag](repeating: 0, count: Int(size) / MemoryLayout<AudioChannelLayoutTag>.size)
        try tags.withUnsafeMutableBytes { buffer in
            try check(AudioFormatGetProperty(kAudioFormatProperty_AvailableEncodeChannelLayoutTags,
                specifierSize, &description, &size, buffer.baseAddress), "AvailableEncodeChannelLayoutTags")
        }
        return tags
    }

    // Channel count alone would choose AAC_7_1 (wide) for a WAVE_7_1 project and lose its rear pair.
    static func selectOutputTag(inputTag: AudioChannelLayoutTag, available: [AudioChannelLayoutTag]) throws -> AudioChannelLayoutTag {
        if available.contains(inputTag) { return inputTag }
        if let match = available.first(where: { hasSameSpeakers(inputTag, $0) }) { return match }
        throw BeutlAVFError.writerFailed("The audio encoder has no layout for the requested speakers (tag \(inputTag)).")
    }

    private static func hasSameSpeakers(_ inputTag: AudioChannelLayoutTag, _ outputTag: AudioChannelLayoutTag) -> Bool {
        let channels = Int(inputTag & 0xffff)
        guard channels > 0, outputTag & 0xffff == UInt32(channels) else { return false }
        var source = layout(inputTag)
        var destination = layout(outputTag)
        return withUnsafePointer(to: &source) { sourcePointer in
            withUnsafePointer(to: &destination) { destinationPointer in
                let layouts = [sourcePointer, destinationPointer]
                var mapping = [Int32](repeating: -1, count: channels)
                var size = UInt32(channels * MemoryLayout<Int32>.size)
                let status = layouts.withUnsafeBufferPointer { specifier in
                    mapping.withUnsafeMutableBytes { buffer in
                        AudioFormatGetProperty(kAudioFormatProperty_ChannelMap,
                            UInt32(2 * MemoryLayout<UnsafePointer<AudioChannelLayout>>.size),
                            specifier.baseAddress, &size, buffer.baseAddress)
                    }
                }
                return status == noErr && Set(mapping) == Set((0..<channels).map { Int32($0) })
            }
        }
    }

    static func aacBitrate(requested: Int32, sampleRate: Int, inputTag: AudioChannelLayoutTag,
        outputTag: AudioChannelLayoutTag) throws -> Int32 {
        guard requested > 0 else { return requested }
        var source = pcmFormat(sampleRate: sampleRate, channelCount: Int(inputTag & 0xffff))
        var destination = AudioStreamBasicDescription()
        destination.mFormatID = kAudioFormatMPEG4AAC
        destination.mSampleRate = Float64(sampleRate)
        destination.mChannelsPerFrame = outputTag & 0xffff
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        try check(AudioFormatGetProperty(kAudioFormatProperty_FormatInfo, 0, nil, &size, &destination), "AAC format")
        var converter: AudioConverterRef?
        try check(AudioConverterNew(&source, &destination, &converter), "AAC converter")
        guard let converter = converter else { throw BeutlAVFError.writerFailed("AAC converter was not created.") }
        defer { AudioConverterDispose(converter) }
        var inputLayout = layout(inputTag)
        var outputLayout = layout(outputTag)
        let layoutSize = UInt32(MemoryLayout<AudioChannelLayout>.size)
        try check(AudioConverterSetProperty(converter, kAudioConverterInputChannelLayout, layoutSize, &inputLayout), "AAC input layout")
        try check(AudioConverterSetProperty(converter, kAudioConverterOutputChannelLayout, layoutSize, &outputLayout), "AAC output layout")
        size = 0
        try check(AudioConverterGetPropertyInfo(converter, kAudioConverterApplicableEncodeBitRates, &size, nil), "AAC bitrates size")
        var ranges = [AudioValueRange](repeating: AudioValueRange(), count: Int(size) / MemoryLayout<AudioValueRange>.size)
        try ranges.withUnsafeMutableBytes { buffer in
            try check(AudioConverterGetProperty(converter, kAudioConverterApplicableEncodeBitRates, &size, buffer.baseAddress!), "AAC bitrates")
        }
        let candidates = ranges.map { max(Double(requested), $0.mMinimum) }.enumerated()
            .filter { $0.element <= ranges[$0.offset].mMaximum }.map { $0.element }
        guard let value = candidates.min(), let bitrate = Int32(exactly: value) else {
            throw BeutlAVFError.writerFailed("AAC does not support the requested bitrate \(requested) for \(destination.mChannelsPerFrame) channels.")
        }
        var accepted = UInt32(bitrate)
        try check(AudioConverterSetProperty(converter, kAudioConverterEncodeBitRate,
            UInt32(MemoryLayout<UInt32>.size), &accepted), "AAC bitrate")
        return bitrate
    }

    private static func check(_ status: OSStatus, _ operation: String) throws {
        guard status == noErr else { throw BeutlAVFError.writerFailed("\(operation) failed: \(status)") }
    }
}
