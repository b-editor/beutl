import AudioToolbox
import XCTest
@testable import BeutlAVF

final class AudioEncoderLayoutTests: XCTestCase {
    func testInputTagsMatchInterleavedWaveOrder() {
        for (count, mask, tag) in [
            (1, UInt32(0x4), kAudioChannelLayoutTag_Mono),
            (2, UInt32(0x3), kAudioChannelLayoutTag_Stereo),
            (6, UInt32(0x3f), kAudioChannelLayoutTag_MPEG_5_1_A),
            (8, UInt32(0x63f), kAudioChannelLayoutTag_WAVE_7_1),
            (12, UInt32(0), kAudioChannelLayoutTag_DiscreteInOrder | 12),
            (6, UInt32(0), kAudioChannelLayoutTag_DiscreteInOrder | 6)
        ] {
            XCTAssertEqual(AudioEncoderLayout.inputTag(channelCount: count, speakerMask: mask), tag)
            let format = AudioEncoderLayout.pcmFormat(sampleRate: 48000, channelCount: count)
            XCTAssertEqual(format.mChannelsPerFrame, UInt32(count))
            XCTAssertEqual(format.mBytesPerFrame, UInt32(count * 4))
            XCTAssertEqual(format.mFormatFlags & kAudioFormatFlagIsNonInterleaved, 0)
        }
    }

    func testOutputSelectsSpeakersInsteadOfOnlyChannelCount() throws {
        XCTAssertEqual(try AudioEncoderLayout.selectOutputTag(inputTag: kAudioChannelLayoutTag_MPEG_5_1_A,
            available: [kAudioChannelLayoutTag_AAC_6_0, kAudioChannelLayoutTag_AAC_5_1]), kAudioChannelLayoutTag_AAC_5_1)
        XCTAssertEqual(try AudioEncoderLayout.selectOutputTag(inputTag: kAudioChannelLayoutTag_WAVE_7_1,
            available: [kAudioChannelLayoutTag_AAC_7_1, kAudioChannelLayoutTag_AAC_7_1_B]), kAudioChannelLayoutTag_AAC_7_1_B)
        XCTAssertEqual(try AudioEncoderLayout.selectOutputTag(inputTag: kAudioChannelLayoutTag_WAVE_7_1,
            available: [kAudioChannelLayoutTag_AAC_7_1_B, kAudioChannelLayoutTag_WAVE_7_1]), kAudioChannelLayoutTag_WAVE_7_1)
    }

    func testOutputRejectsLayoutsThatLoseSpeakers() {
        XCTAssertThrowsError(try AudioEncoderLayout.selectOutputTag(inputTag: kAudioChannelLayoutTag_WAVE_7_1,
            available: [kAudioChannelLayoutTag_AAC_7_1]))
    }

    func testAvailableAacTagsAndMinimumBitrate() throws {
        for (channels, inputTag) in [(6, kAudioChannelLayoutTag_MPEG_5_1_A), (8, kAudioChannelLayoutTag_WAVE_7_1)] {
            let tags = try AudioEncoderLayout.availableEncodeTags(format: kAudioFormatMPEG4AAC, channelCount: channels)
            let outputTag = try AudioEncoderLayout.selectOutputTag(inputTag: inputTag, available: tags)
            let minimum = try AudioEncoderLayout.aacBitrate(requested: 1000, sampleRate: 48000,
                inputTag: inputTag, outputTag: outputTag)
            XCTAssertGreaterThan(minimum, 1000)
            XCTAssertEqual(try AudioEncoderLayout.aacBitrate(requested: minimum, sampleRate: 48000,
                inputTag: inputTag, outputTag: outputTag), minimum)
        }
    }
}
