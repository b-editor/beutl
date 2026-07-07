using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

using NUnit.Framework;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public sealed class ProxyMediaReaderTests
{
    [Test]
    public void Dispose_DisposesInnerReaderBeforePin()
    {
        var disposeOrder = new List<string>();
        var inner = new FakeMediaReader(() => disposeOrder.Add("inner"));
        var pin = new FakePin(() => disposeOrder.Add("pin"));

        var resolution = new ProxyResolution(
            AbsoluteProxyFilePath: "/proxy.mp4",
            Source: new ProxyFingerprint("/source.mp4", 100, DateTime.UtcNow),
            Preset: ProxyPreset.Quarter,
            OriginalLogicalFrameSize: new PixelSize(100, 80),
            ProxyDecodedFrameSize: new PixelSize(50, 40));

        var reader = new ProxyMediaReader(inner, pin, resolution);
        Assert.Multiple(() =>
        {
            Assert.That(reader.ProxyResolution, Is.SameAs(resolution));
            Assert.That(reader.HasVideo, Is.EqualTo(inner.HasVideo));
            Assert.That(reader.HasAudio, Is.EqualTo(inner.HasAudio));
        });

        reader.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(disposeOrder, Is.EqualTo(new[] { "inner", "pin" }),
                "inner.Dispose() must run before pin.Dispose() so eviction cannot delete a file mid-decode.");
            Assert.That(inner.IsDisposed, Is.True);
        });
    }

    private sealed class FakeMediaReader(Action? onDispose = null) : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new VideoStreamInfo(
            codecName: "test",
            duration: new Rational(1),
            frameSize: new PixelSize(50, 40),
            frameRate: new Rational(30, 1));

        public override AudioStreamInfo AudioInfo { get; } = new AudioStreamInfo(
            CodecName: "test",
            Duration: Rational.Zero,
            SampleRate: 44100,
            NumChannels: 2);

        public override bool HasVideo => true;
        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                onDispose?.Invoke();
            base.Dispose(disposing);
        }
    }

    private sealed class FakePin(Action? onDispose) : IDisposable
    {
        public void Dispose() => onDispose?.Invoke();
    }
}