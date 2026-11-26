using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Graphics;
using Beutl.Audio;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class SourceVideoThumbnailTests
{
    // Regression for Project #9 item #240: a degenerate frame size made the count 0, the interval +Infinity, and
    // TimeSpan.FromSeconds overflow. GetVideoThumbnailCount returns 0 so the caller bails out before that math.
    [TestCase(1920, 0, TestName = "ZeroHeight")]
    [TestCase(1920, -1, TestName = "NegativeHeight")]
    [TestCase(0, 1080, TestName = "ZeroWidth")]
    [TestCase(-1, 1080, TestName = "NegativeWidth")]
    public void GetVideoThumbnailCount_ReturnsZero_ForDegenerateFrameSize(int width, int height)
    {
        int count = SourceVideo.GetVideoThumbnailCount(new PixelSize(width, height), maxWidth: 1920, maxHeight: 25);

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void GetVideoThumbnailCount_ReturnsZero_ForNonPositiveMaxDimensions()
    {
        var frame = new PixelSize(1920, 1080);
        Assert.That(SourceVideo.GetVideoThumbnailCount(frame, maxWidth: 0, maxHeight: 25), Is.Zero);
        Assert.That(SourceVideo.GetVideoThumbnailCount(frame, maxWidth: 1920, maxHeight: 0), Is.Zero);
    }

    [Test]
    public void GetVideoThumbnailCount_ComputesExpectedCount_ForValidFrameSize()
    {
        // 16:9 frame, maxWidth 1920, thumbnail height 25:
        //   thumbWidth = 25 * (1920 / 1080) ≈ 44.44, count = ceil(1920 / 44.44) = 44.
        int count = SourceVideo.GetVideoThumbnailCount(new PixelSize(1920, 1080), maxWidth: 1920, maxHeight: 25);

        Assert.That(count, Is.EqualTo(44));
    }

    [Test]
    public async Task VisibleStripUsesOnlySelectedSlotsAndReusesSourceFramesAfterEdits()
    {
        using var decoder = new CountingDecoder();
        var cache = new ThumbnailCacheService();
        SourceVideo video = Clip(0, 6);
        var initial = await Read(video, 25, 300, [1, 3]);
        try
        {
            Assert.That(initial.Select(frame => frame.Index), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(initial.Select(frame => frame.Count), Is.EqualTo(new[] { 6, 6 }));
            Assert.That(decoder.ReadFrames, Is.EqualTo(new[] { 30, 90 }));
            Assert.That(decoder.Opens, Has.All.Matches<MediaOptions>(options => options is { IsBackground: true, VideoThumbnailHeight: 32 }));

            // A split/left-trim takes frame 30 to slot zero. A slip then moves that slot to frame 90.
            SourceVideo shortened = Clip(1, 2);
            var trimmed = await Read(shortened, 31, 120, [0]);
            using var trimmedFrame = trimmed.Single().Thumbnail;
            Assert.That(trimmedFrame.Value, Is.SameAs(initial[0].Thumbnail.Value));

            shortened.OffsetPosition.Value = MediaTime.FromSeconds(3);
            shortened.Speed.Value = 150;
            var slipped = await Read(shortened, 31, 120, [0]);
            using var slippedFrame = slipped.Single().Thumbnail;
            Assert.That(slippedFrame.Value, Is.SameAs(initial[1].Thumbnail.Value));
            Assert.That(decoder.ReadFrames, Is.EqualTo(new[] { 30, 90 }));

            var taller = await Read(shortened, 48, 120, [0]);
            using var tallerFrame = taller.Single().Thumbnail;
            Assert.That(tallerFrame.Value.Height, Is.EqualTo(48));
            Assert.That(tallerFrame.Value, Is.Not.SameAs(slippedFrame.Value));
            Assert.That(decoder.ReadFrames, Is.EqualTo(new[] { 30, 90, 90 }));
            Assert.Multiple(() =>
            {
                Assert.That(slippedFrame.Value.Height, Is.EqualTo(32));
                Assert.That(slippedFrame.Value.Width, Is.EqualTo(64));
                Assert.That(slippedFrame.Value.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
                Assert.That(slippedFrame.Value.AlphaType, Is.EqualTo(BitmapAlphaType.Premul));
                Assert.That(slippedFrame.Value.ColorSpace, Is.EqualTo(BitmapColorSpace.Srgb));
            });
        }
        finally
        {
            foreach (var frame in initial)
                frame.Thumbnail.Dispose();
        }

        SourceVideo Clip(double offset, double duration) => new()
        {
            Source = { Value = VideoSource.Open(decoder.Path) },
            OffsetPosition = { Value = MediaTime.FromSeconds(offset) },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(12), MediaTime.FromSeconds(duration)) },
        };

        async Task<List<(int Index, int Count, Ref<Bitmap> Thumbnail)>> Read(
            SourceVideo clip, int height, int width, int[] indices)
        {
            var frames = new List<(int, int, Ref<Bitmap>)>();
            await foreach (var (index, count, image) in clip.GetVisibleThumbnailLeasesAsync(
                               TestFonts.Manager, width, height, cache, CancellationToken.None, false,
                               ProxyPreset.Quarter, decoder.Decoders, _ => ValueTask.FromResult<IReadOnlyList<int>>(indices)))
            {
                Assert.That(image, Is.Not.Null);
                frames.Add((index, count, image!));
            }
            return frames;
        }
    }

    [Test]
    public async Task ThumbnailAppliesTheSelectedTransferCurveOnCpu()
    {
        using var decoder = new CountingDecoder();
        var video = new SourceVideo
        {
            Source = { Value = VideoSource.Open(decoder.Path) },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) },
            OffsetPosition = { Value = MediaTime.FromFrames(12, new Rational(30, 1)) },
        };
        var cache = new ThumbnailCacheService();
        byte srgb = await Read(VideoTransferInterpretation.Srgb);
        byte bt1886 = await Read(VideoTransferInterpretation.Bt1886);
        Assert.That(srgb, Is.EqualTo(120).Within(1));
        Assert.That(bt1886, Is.LessThan(srgb));
        Assert.That(decoder.ReadFrames, Is.EqualTo(new[] { 12, 12 }), "interpretations have distinct cache entries");

        async Task<byte> Read(VideoTransferInterpretation interpretation)
        {
            video.TransferInterpretation.Value = interpretation;
            await foreach (var (_, _, thumbnail) in video.GetThumbnailStripAsync(
                               TestFonts.Manager, 128, 30, cache, endIndex: 0, decoders: decoder.Decoders))
            {
                using (thumbnail)
                    return thumbnail.GetRow<Bgra8888>(0)[0].G;
            }
            throw new AssertionException("The source did not yield a thumbnail.");
        }
    }

    [Test]
    public void CacheBudgetsAreIndependent_AndAnEvictedBitmapRemainsAliveWhileLeased()
    {
        using var first = Ref<Bitmap>.Create(new Bitmap(4, 4));
        using var second = Ref<Bitmap>.Create(new Bitmap(4, 4));
        var cache = new ThumbnailCacheService(first.Value.ByteCount, waveformEntryBudget: 2);
        cache.SaveVideo("video", MediaTime.Zero, first);
        Bitmap pixels = first.Value;
        first.Dispose();
        using var lease = cache.AcquireVideo("video", MediaTime.Zero);
        Assert.That(lease?.Value, Is.SameAs(pixels));

        for (int i = 0; i < 600; i++)
            cache.SaveWaveform("audio", new MediaTime(i), -i, i);

        using (var afterWaveforms = cache.AcquireVideo("video", MediaTime.Zero))
            Assert.That(afterWaveforms?.Value, Is.SameAs(pixels));
        Assert.That(cache.TryGetWaveform("audio", MediaTime.Zero, MediaTime.Zero, out _, out _), Is.False);
        Assert.That(cache.TryGetWaveform("audio", new MediaTime(599), MediaTime.Zero, out _, out _), Is.True);

        cache.SaveVideo("video", new MediaTime(1), second);
        Assert.That(cache.AcquireVideo("video", MediaTime.Zero), Is.Null);
        Assert.That(pixels.IsDisposed, Is.False);
        Assert.That(cache.TryGetWaveform("audio", new MediaTime(599), MediaTime.Zero, out _, out _), Is.True);
        lease!.Dispose();
        Assert.That(pixels.IsDisposed, Is.True);
        cache.Invalidate("video");
    }

    private sealed class CountingDecoder : IDecoderInfo, IDisposable
    {
        public CountingDecoder()
        {
            File.WriteAllBytes(Path, [1]);
            Decoders.Register(this);
        }

        public string Path { get; } = System.IO.Path.GetTempFileName();
        public DecoderRegistry Decoders { get; } = new(new());
        public List<int> ReadFrames { get; } = [];
        public List<MediaOptions> Opens { get; } = [];
        public string Name => "Video thumbnail test decoder";

        public IEnumerable<string> AudioExtensions() => [];
        public IEnumerable<string> VideoExtensions() => [".tmp"];
        public bool IsSupported(string file) => file == Path;

        public MediaReader Open(string file, MediaOptions options)
        {
            Opens.Add(options);
            return new Reader(ReadFrames);
        }

        public void Dispose() => File.Delete(Path);

        private sealed class Reader(List<int> frames) : MediaReader
        {
            private readonly TestMediaReader _reader = new(new PixelSize(128, 64), new Rational(30, 1), 240);

            public override VideoStreamInfo VideoInfo => _reader.VideoInfo;
            public override AudioStreamInfo AudioInfo => _reader.AudioInfo;
            public override bool HasVideo => true;
            public override bool HasAudio => false;

            public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
            {
                frames.Add(frame);
                return _reader.ReadVideo(frame, out image);
            }

            public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
                => _reader.ReadAudio(start, length, out sound);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _reader.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
