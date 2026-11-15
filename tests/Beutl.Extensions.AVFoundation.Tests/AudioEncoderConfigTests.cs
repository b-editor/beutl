using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Extensions.AVFoundation.Interop;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
public class AudioEncoderConfigTests
{
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC, 1, true)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC, 2, true)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC, 6, true)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC, 4, false)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC, 8, false)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.LinearPCM, 8, true)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.LinearPCM, 12, true)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.AppleLossless, 6, true)]
    [TestCase(AVFAudioEncoderSettings.AudioFormatType.AppleLossless, 8, true)]
    public void ChannelValidationFollowsTheCodec(AVFAudioEncoderSettings.AudioFormatType format, int channels, bool accepted)
    {
        Assert.That(() => AVFEncodingController.ValidateAudioChannels(format, channels),
            accepted ? Throws.Nothing : Throws.TypeOf<NotSupportedException>().With.Message.EqualTo(
                "AVFoundation AAC supports 1, 2 or 6 channels. Choose Linear PCM, or the FFmpeg encoder, for other channel counts."));
    }

    // ABI 6 dropped the sample rate converter quality AVFoundation never applied, and carries the output's default
    // layout from AudioChannelLayout.GetDefault instead of a copy of it in Swift (#3900).
    [TestCase(2, 0x3u)]
    [TestCase(4, 0x33u)]
    [TestCase(8, 0x63fu)]
    public void EncoderConfigCarriesTheInputLayoutAndTheOutputsDefault(int channels, uint outputMask)
    {
        var controller = new AVFEncodingController("test.mp4");
        controller.AudioSettings.Channels.Value = channels;
        var config = controller.BuildAudioConfig(48000, AudioChannelLayout.Surround51);
        Assert.Multiple(() =>
        {
            Assert.That(BeutlAVFNative.ExpectedAbiVersion, Is.EqualTo(6));
            Assert.That(Marshal.SizeOf<BeutlAudioEncoderConfig>(), Is.EqualTo(40));
            Assert.That(Marshal.OffsetOf<BeutlAudioEncoderConfig>(nameof(BeutlAudioEncoderConfig.LinearPcmBitDepth)).ToInt32(), Is.EqualTo(20));
            Assert.That(Marshal.OffsetOf<BeutlAudioEncoderConfig>(nameof(BeutlAudioEncoderConfig.InputChannelCount)).ToInt32(), Is.EqualTo(28));
            Assert.That(Marshal.OffsetOf<BeutlAudioEncoderConfig>(nameof(BeutlAudioEncoderConfig.InputSpeakerMask)).ToInt32(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<BeutlAudioEncoderConfig>(nameof(BeutlAudioEncoderConfig.OutputSpeakerMask)).ToInt32(), Is.EqualTo(36));
            Assert.That(config.InputChannelCount, Is.EqualTo(6));
            Assert.That(config.InputSpeakerMask, Is.EqualTo(0x3fu));
            Assert.That(config.ChannelCount, Is.EqualTo(channels));
            Assert.That(config.OutputSpeakerMask, Is.EqualTo(outputMask));
        });
    }
}
