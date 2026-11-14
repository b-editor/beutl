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

    // The frame message names the frame's color space, which comes through the pipe intact (#4291).
    [TestCase(false)]
    [TestCase(true)]
    public async Task ProvideFrame_CarriesTheFrameColorSpace(bool hdr)
    {
        BitmapColorSpace colorSpace = hdr
            ? BitmapColorSpaceMapping.BuildHdrColorSpace(BitmapColorTransfer.Hlg, BitmapColorPrimaries.Rec2020)
            : BitmapColorSpaceMapping.BuildTargetColorSpace(BitmapColorTransfer.Bt709, BitmapColorPrimaries.Bt709);
        using var bitmap = new Bitmap(2, 1, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, colorSpace);
        using var stream = new MemoryStream();
        await MessageSerializer.WriteMessageAsync(stream,
            IpcMessage.Create(1, MessageType.ProvideFrame, FFmpegEncodingControllerProxy.CreateFrameMessage(bitmap)));
        stream.Position = 0;
        var received = (await MessageSerializer.ReadMessageAsync(stream))!.RequirePayload<ProvideFrameMessage>(MessageType.ProvideFrame);

        Assert.That(IpcColorSpace.FromArrays(received.TransferFn!, received.ToXyzD50!), Is.EqualTo(colorSpace));
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
