using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Extensions.AVFoundation.Interop;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
public class AudioEncoderConfigTests
{
    [Test]
    public void EncoderConfigAppendsProviderLayoutAtAbi5()
    {
        var controller = new AVFEncodingController("test.mp4");
        controller.AudioSettings.Channels.Value = 2;
        var config = controller.BuildAudioConfig(48000, AudioChannelLayout.Surround51);
        Assert.Multiple(() =>
        {
            Assert.That(BeutlAVFNative.ExpectedAbiVersion, Is.EqualTo(5));
            Assert.That(Marshal.SizeOf<BeutlAudioEncoderConfig>(), Is.EqualTo(40));
            Assert.That(Marshal.OffsetOf<BeutlAudioEncoderConfig>(nameof(BeutlAudioEncoderConfig.InputChannelCount)).ToInt32(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<BeutlAudioEncoderConfig>(nameof(BeutlAudioEncoderConfig.InputSpeakerMask)).ToInt32(), Is.EqualTo(36));
            Assert.That(config.InputChannelCount, Is.EqualTo(6));
            Assert.That(config.InputSpeakerMask, Is.EqualTo(0x3fu));
            Assert.That(config.ChannelCount, Is.EqualTo(2));
        });
    }
}
