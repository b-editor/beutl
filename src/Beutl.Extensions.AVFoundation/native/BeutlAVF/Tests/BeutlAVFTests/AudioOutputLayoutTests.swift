import AudioToolbox
import XCTest
@testable import BeutlAVF

final class AudioOutputLayoutTests: XCTestCase {
    func testMissingLayoutOnlyAssignsMonoAndStereoPositions() {
        for (count, mask, tag) in [
            (1, UInt32(0x4), kAudioChannelLayoutTag_Mono),
            (2, UInt32(0x3), kAudioChannelLayoutTag_Stereo),
            (6, UInt32(0), kAudioChannelLayoutTag_DiscreteInOrder | 6)
        ] {
            let output = AudioOutputLayout(channelCount: count, sourceLayout: nil, layoutSize: 0)
            XCTAssertEqual(output.channelCount, count)
            XCTAssertEqual(output.speakerMask, mask)
            XCTAssertEqual(outputTag(output), tag)
        }
    }

    func testMissingChannelCountKeepsStereoWithoutRequestingALayout() {
        let output = AudioOutputLayout(channelCount: nil, sourceLayout: nil, layoutSize: 0)
        XCTAssertEqual(output.channelCount, 2)
        XCTAssertEqual(output.speakerMask, 0x3)
        XCTAssertNil(output.data)
    }

    func testBitmapUsesWaveSpeakerBits() {
        var layout = AudioChannelLayout()
        layout.mChannelLayoutTag = kAudioChannelLayoutTag_UseChannelBitmap
        layout.mChannelBitmap = AudioChannelBitmap(rawValue: 0x63f)
        let output = makeOutput(&layout, channels: 8)
        XCTAssertEqual(output.speakerMask, 0x63f)
        XCTAssertEqual(outputLabels(output), [kAudioChannelLabel_Left, kAudioChannelLabel_Right,
            kAudioChannelLabel_Center, kAudioChannelLabel_LFEScreen,
            kAudioChannelLabel_LeftSurround, kAudioChannelLabel_RightSurround,
            kAudioChannelLabel_LeftSurroundDirect, kAudioChannelLabel_RightSurroundDirect])
    }

    func testConcreteTagsKeepSourceLabelsInWaveOrder() {
        for (inputTag, mask, labels) in [
            (kAudioChannelLayoutTag_Mono, UInt32(0x4), [kAudioChannelLabel_Mono]),
            (kAudioChannelLayoutTag_Stereo, UInt32(0x3), [kAudioChannelLabel_Left, kAudioChannelLabel_Right]),
            (kAudioChannelLayoutTag_MPEG_7_1_C, UInt32(0x63f), [kAudioChannelLabel_Left, kAudioChannelLabel_Right,
                kAudioChannelLabel_Center, kAudioChannelLabel_LFEScreen,
                kAudioChannelLabel_RearSurroundLeft, kAudioChannelLabel_RearSurroundRight,
                kAudioChannelLabel_LeftSurround, kAudioChannelLabel_RightSurround])
        ] {
            var layout = AudioChannelLayout()
            layout.mChannelLayoutTag = inputTag
            let output = makeOutput(&layout, channels: labels.count)
            XCTAssertEqual(output.speakerMask, mask)
            XCTAssertEqual(outputLabels(output), labels)
        }
    }

    func testWave51KeepsRearSurroundLabels() {
        var layout = AudioChannelLayout()
        layout.mChannelLayoutTag = kAudioChannelLayoutTag_WAVE_5_1_B
        let output = makeOutput(&layout, channels: 6)
        XCTAssertEqual(output.speakerMask, 0x3f)
        XCTAssertEqual(outputLabels(output), [kAudioChannelLabel_Left, kAudioChannelLabel_Right,
            kAudioChannelLabel_Center, kAudioChannelLabel_LFEScreen,
            kAudioChannelLabel_RearSurroundLeft, kAudioChannelLabel_RearSurroundRight])
    }

    func testAac51ReordersWithoutReplacingSurroundLabels() {
        var layout = AudioChannelLayout()
        // AAC stores C L R Ls Rs LFE; the requested descriptions must be L R C LFE Ls Rs.
        layout.mChannelLayoutTag = kAudioChannelLayoutTag_AAC_5_1
        let output = makeOutput(&layout, channels: 6)
        XCTAssertEqual(output.speakerMask, 0x3f)
        XCTAssertEqual(outputLabels(output), [kAudioChannelLabel_Left, kAudioChannelLabel_Right,
            kAudioChannelLabel_Center, kAudioChannelLabel_LFEScreen,
            kAudioChannelLabel_LeftSurround, kAudioChannelLabel_RightSurround])
    }

    func testDescriptionsMapSurroundPairsByPresenceOfRearLabels() {
        let front: [AudioChannelLabel] = [kAudioChannelLabel_Left, kAudioChannelLabel_Right,
            kAudioChannelLabel_Center, kAudioChannelLabel_LFEScreen]
        let surround: [AudioChannelLabel] = [kAudioChannelLabel_LeftSurround, kAudioChannelLabel_RightSurround]
        let rear: [AudioChannelLabel] = [kAudioChannelLabel_RearSurroundLeft, kAudioChannelLabel_RearSurroundRight]
        let fiveOne = makeOutput(labels: front + surround)
        XCTAssertEqual(fiveOne.speakerMask, 0x3f)
        XCTAssertEqual(outputLabels(fiveOne), front + surround)
        let sevenOne = makeOutput(labels: front + surround + rear)
        XCTAssertEqual(sevenOne.speakerMask, 0x63f)
        XCTAssertEqual(outputLabels(sevenOne), front + rear + surround)
    }

    func testOtherMasksSortDescriptionsByWaveBit() {
        // Original labels 1...18 cover every WAVE speaker, including direct surrounds and height.
        let labels = Array(AudioChannelLabel(1)...AudioChannelLabel(18))
        let output = makeOutput(labels: labels.reversed())
        XCTAssertEqual(output.speakerMask, 0x3ffff)
        XCTAssertEqual(outputTag(output), kAudioChannelLayoutTag_UseChannelDescriptions)
        XCTAssertEqual(outputLabels(output), labels)
    }

    func testRearHeightAliasesUseWaveTopBackBits() {
        let output = makeOutput(labels: [kAudioChannelLabel_RightTopRear, kAudioChannelLabel_LeftTopRear,
            kAudioChannelLabel_CenterTopRear])
        XCTAssertEqual(output.speakerMask, 0x38000)
        XCTAssertEqual(outputLabels(output), [kAudioChannelLabel_LeftTopRear, kAudioChannelLabel_CenterTopRear,
            kAudioChannelLabel_RightTopRear])
    }

    func testUnmappableOrDuplicateLabelsStayDiscrete() {
        for labels: [AudioChannelLabel] in [
            [kAudioChannelLabel_Left, kAudioChannelLabel_Unknown],
            [kAudioChannelLabel_Discrete_0, kAudioChannelLabel_Discrete_1],
            [kAudioChannelLabel_Left, kAudioChannelLabel_Left],
            [kAudioChannelLabel_LeftTopMiddle, kAudioChannelLabel_RightTopMiddle]
        ] {
            let output = makeOutput(labels: labels)
            XCTAssertEqual(output.speakerMask, 0)
            XCTAssertEqual(outputTag(output), kAudioChannelLayoutTag_DiscreteInOrder | 2)
        }
    }

    func testDiscreteTagAndMismatchedCountStayDiscrete() {
        for tag in [kAudioChannelLayoutTag_DiscreteInOrder | 6, kAudioChannelLayoutTag_Stereo] {
            var layout = AudioChannelLayout()
            layout.mChannelLayoutTag = tag
            let output = makeOutput(&layout, channels: 6)
            XCTAssertEqual(output.speakerMask, 0)
            XCTAssertEqual(outputTag(output), kAudioChannelLayoutTag_DiscreteInOrder | 6)
        }
    }

    private func makeOutput(_ layout: inout AudioChannelLayout, channels: Int) -> AudioOutputLayout {
        withUnsafePointer(to: &layout) {
            AudioOutputLayout(channelCount: channels, sourceLayout: $0, layoutSize: MemoryLayout<AudioChannelLayout>.size)
        }
    }

    private func makeOutput(labels: [AudioChannelLabel]) -> AudioOutputLayout {
        let offset = MemoryLayout<AudioChannelLayout>.offset(of: \.mChannelDescriptions)!
        let size = offset + labels.count * MemoryLayout<AudioChannelDescription>.stride
        let storage = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: MemoryLayout<AudioChannelLayout>.alignment)
        defer { storage.deallocate() }
        storage.initializeMemory(as: UInt8.self, repeating: 0, count: size)
        let layout = storage.assumingMemoryBound(to: AudioChannelLayout.self)
        layout.pointee.mChannelLayoutTag = kAudioChannelLayoutTag_UseChannelDescriptions
        layout.pointee.mNumberChannelDescriptions = UInt32(labels.count)
        let descriptions = storage.advanced(by: offset).assumingMemoryBound(to: AudioChannelDescription.self)
        for (index, label) in labels.enumerated() {
            descriptions[index].mChannelLabel = label
        }
        return AudioOutputLayout(channelCount: labels.count, sourceLayout: UnsafePointer(layout), layoutSize: size)
    }

    private func outputTag(_ output: AudioOutputLayout) -> AudioChannelLayoutTag {
        output.data!.withUnsafeBytes { $0.loadUnaligned(as: AudioChannelLayoutTag.self) }
    }

    private func outputLabels(_ output: AudioOutputLayout) -> [AudioChannelLabel] {
        guard outputTag(output) == kAudioChannelLayoutTag_UseChannelDescriptions else {
            XCTFail("Positioned output must request the source labels as channel descriptions.")
            return []
        }
        let offset = MemoryLayout<AudioChannelLayout>.offset(of: \.mChannelDescriptions)!
        return output.data!.withUnsafeBytes { bytes in
            (0..<output.channelCount).map {
                bytes.loadUnaligned(fromByteOffset: offset + $0 * MemoryLayout<AudioChannelDescription>.stride,
                    as: AudioChannelDescription.self).mChannelLabel
            }
        }
    }
}
