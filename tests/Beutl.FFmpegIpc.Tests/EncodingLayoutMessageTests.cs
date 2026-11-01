using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public class EncodingLayoutMessageTests
{
    [TestCase(2, 3u)]
    [TestCase(6, 0x3fu)]
    [TestCase(6, 0x60fu)]
    [TestCase(8, 0x63fu)]
    [TestCase(12, 0u)]
    public async Task StartEncode_CarriesTheProviderLayoutIndependentlyOfOutputChannels(int channels, uint mask)
    {
        var layout = new AudioChannelLayout(channels, (AudioSpeakers)mask);
        using var provider = new Provider(layout);
        var controller = new FFmpegEncodingControllerProxy("test.wav", new FFmpegEncodingSettings());
        controller.AudioSettings.Channels.Value = 1;
        var request = controller.CreateStartRequest(provider, provider);
        using var stream = new MemoryStream();
        await MessageSerializer.WriteMessageAsync(stream, IpcMessage.Create(1, MessageType.StartEncode, request));
        stream.Position = 0;
        var received = (await MessageSerializer.ReadMessageAsync(stream))!.RequirePayload<EncodeStartRequest>(MessageType.StartEncode);
        Assert.Multiple(() =>
        {
            Assert.That(received.ProviderChannelCount, Is.EqualTo(channels));
            Assert.That(received.ProviderSpeakerMask, Is.EqualTo(mask));
            Assert.That(received.AudioChannels, Is.EqualTo(1));
        });
    }

    private sealed class Provider(AudioChannelLayout layout) : ISampleProvider, IFrameProvider
    {
        public AudioChannelLayout ChannelLayout => layout;
        public long SampleCount => 0;
        public long SampleRate => 48000;
        public long FrameCount => 0;
        public Rational FrameRate => new(30);
        public ValueTask<AudioBuffer?> ReadNextSamples(int length) => ValueTask.FromResult<AudioBuffer?>(null);
        public ValueTask<Bitmap?> ReadNextFrame() => ValueTask.FromResult<Bitmap?>(null);
        public void Dispose() { }
    }
}
