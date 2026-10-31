import AudioToolbox
import Foundation

// The reader returns interleaved channels in ascending WAVEFORMATEXTENSIBLE speaker-bit order.
struct AudioOutputLayout {
    let channelCount: Int
    let speakerMask: UInt32
    let data: Data?

    init(channelCount: Int?, sourceLayout: UnsafePointer<AudioChannelLayout>?, layoutSize: Int) {
        guard let channelCount = channelCount, channelCount > 0 else {
            // Preserve the reader's stereo conversion when no native format information is available.
            self.channelCount = 2
            speakerMask = 0x3
            data = nil
            return
        }
        self.channelCount = channelCount
        let labels: [AudioChannelLabel]?
        if let sourceLayout = sourceLayout {
            labels = Self.channelLabels(sourceLayout, size: layoutSize)
        } else {
            // Mono and stereo have conventional positions even when the file omits a layout.
            labels = channelCount == 1 ? [kAudioChannelLabel_Center]
                : channelCount == 2 ? [kAudioChannelLabel_Left, kAudioChannelLabel_Right] : nil
        }

        let hasRearSurround = labels?.contains {
            $0 == kAudioChannelLabel_RearSurroundLeft || $0 == kAudioChannelLabel_RearSurroundRight
        } ?? false
        var channels: [(bit: UInt32, label: AudioChannelLabel)] = []
        var mask: UInt32 = 0
        if let labels = labels, labels.count == channelCount {
            for label in labels {
                let bit = Self.speakerBit(label, hasRearSurround: hasRearSurround)
                guard bit != 0, mask & bit == 0 else {
                    channels.removeAll()
                    break
                }
                mask |= bit
                channels.append((bit, label))
            }
        }

        guard channels.count == channelCount else {
            speakerMask = 0
            data = Self.tagData(kAudioChannelLayoutTag_DiscreteInOrder | UInt32(channelCount))
            return
        }

        speakerMask = mask
        if sourceLayout == nil {
            // Only the synthesized mono/stereo labels use a default tag.
            data = Self.tagData(channelCount == 1 ? kAudioChannelLayoutTag_Mono : kAudioChannelLayoutTag_Stereo)
        } else {
            // Keep the source labels: replacing rear-surround labels with surround labels can silence them.
            data = Self.descriptionData(channels.sorted { $0.bit < $1.bit }.map { $0.label })
        }
    }

    private static let descriptionsOffset = MemoryLayout<AudioChannelLayout>.offset(of: \.mChannelDescriptions)!

    private static func channelLabels(_ layout: UnsafePointer<AudioChannelLayout>, size: Int) -> [AudioChannelLabel]? {
        guard size >= descriptionsOffset else { return nil }
        let tag = layout.pointee.mChannelLayoutTag
        if tag == kAudioChannelLayoutTag_UseChannelDescriptions {
            let count = Int(layout.pointee.mNumberChannelDescriptions)
            guard count <= (size - descriptionsOffset) / MemoryLayout<AudioChannelDescription>.stride else { return nil }
            let descriptions = UnsafeRawPointer(layout).advanced(by: descriptionsOffset)
                .assumingMemoryBound(to: AudioChannelDescription.self)
            return (0..<count).map { descriptions[$0].mChannelLabel }
        }

        // Expand both bitmap layouts and concrete tags; their stored order can differ from WAVE order.
        let property = tag == kAudioChannelLayoutTag_UseChannelBitmap
            ? kAudioFormatProperty_ChannelLayoutForBitmap : kAudioFormatProperty_ChannelLayoutForTag
        var specifier = tag == kAudioChannelLayoutTag_UseChannelBitmap
            ? layout.pointee.mChannelBitmap.rawValue : tag
        var outputSize: UInt32 = 0
        guard AudioFormatGetPropertyInfo(property, 4, &specifier, &outputSize) == noErr,
              outputSize >= descriptionsOffset else { return nil }
        let storage = UnsafeMutableRawPointer.allocate(
            byteCount: Int(outputSize), alignment: MemoryLayout<AudioChannelLayout>.alignment)
        defer { storage.deallocate() }
        guard AudioFormatGetProperty(property, 4, &specifier, &outputSize, storage) == noErr else { return nil }
        let expanded = storage.assumingMemoryBound(to: AudioChannelLayout.self)
        // ChannelLayoutForTag populates descriptions while retaining the original concrete tag.
        expanded.pointee.mChannelLayoutTag = kAudioChannelLayoutTag_UseChannelDescriptions
        return channelLabels(UnsafePointer(expanded), size: Int(outputSize))
    }

    private static func speakerBit(_ label: AudioChannelLabel, hasRearSurround: Bool) -> UInt32 {
        switch label {
        case kAudioChannelLabel_LeftSurround:
            return hasRearSurround ? 0x200 : 0x10
        case kAudioChannelLabel_RightSurround:
            return hasRearSurround ? 0x400 : 0x20
        case kAudioChannelLabel_RearSurroundLeft:
            return 0x10
        case kAudioChannelLabel_RearSurroundRight:
            return 0x20
        case kAudioChannelLabel_Mono:
            return 0x4
        case kAudioChannelLabel_LeftTopRear:
            return 0x8000
        case kAudioChannelLabel_CenterTopRear:
            return 0x10000
        case kAudioChannelLabel_RightTopRear:
            return 0x20000
        // Labels 1...18 match the WAVE bits, including direct surrounds and the original top labels.
        case kAudioChannelLabel_Left...kAudioChannelLabel_TopBackRight:
            return 1 << (label - 1)
        default:
            // Discrete, coordinates, ambisonics, and positions without a WAVE equivalent stay discrete.
            return 0
        }
    }

    private static func tagData(_ tag: AudioChannelLayoutTag) -> Data {
        var layout = AudioChannelLayout()
        layout.mChannelLayoutTag = tag
        return withUnsafeBytes(of: &layout) { Data($0) }
    }

    private static func descriptionData(_ labels: [AudioChannelLabel]) -> Data {
        let size = descriptionsOffset + labels.count * MemoryLayout<AudioChannelDescription>.stride
        let storage = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: MemoryLayout<AudioChannelLayout>.alignment)
        defer { storage.deallocate() }
        storage.initializeMemory(as: UInt8.self, repeating: 0, count: size)
        let layout = storage.assumingMemoryBound(to: AudioChannelLayout.self)
        layout.pointee.mChannelLayoutTag = kAudioChannelLayoutTag_UseChannelDescriptions
        layout.pointee.mNumberChannelDescriptions = UInt32(labels.count)
        let descriptions = storage.advanced(by: descriptionsOffset).assumingMemoryBound(to: AudioChannelDescription.self)
        for (index, label) in labels.enumerated() {
            descriptions[index].mChannelLabel = label
        }
        return Data(bytes: storage, count: size)
    }
}
