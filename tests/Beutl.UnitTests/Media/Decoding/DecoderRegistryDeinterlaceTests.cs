using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Media.Decoding;

[TestFixture]
public sealed class DecoderRegistryDeinterlaceTests
{
    private static readonly VideoDeinterlaceOptions s_fields = new(DeinterlaceMode.Auto,
        DeinterlaceFieldOrder.Auto, DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
    private const DecoderCapabilities Capabilities = DecoderCapabilities.DeinterlaceAuto | DecoderCapabilities.DeinterlaceExplicit
        | DecoderCapabilities.DeinterlaceBwdif | DecoderCapabilities.DeinterlaceDoubleRate;
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.GetTempFileName();

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [TestCase(DeinterlaceMode.Off, VideoFieldOrder.TopFieldFirst, false, DeinterlaceApplicationStatus.Off)]
    [TestCase(DeinterlaceMode.Auto, VideoFieldOrder.Progressive, false, DeinterlaceApplicationStatus.NotNeededProgressive)]
    [TestCase(DeinterlaceMode.Auto, VideoFieldOrder.TopFieldFirst, true, DeinterlaceApplicationStatus.Applied)]
    [TestCase(DeinterlaceMode.Auto, VideoFieldOrder.Mixed, true, DeinterlaceApplicationStatus.Applied)]
    [TestCase(DeinterlaceMode.Auto, VideoFieldOrder.Unknown, true, DeinterlaceApplicationStatus.Applied)]
    [TestCase(DeinterlaceMode.On, VideoFieldOrder.Progressive, true, DeinterlaceApplicationStatus.Applied)]
    public void RoutesByModeAndDetectedScanType(DeinterlaceMode mode, VideoFieldOrder nativeOrder,
        bool usesCapableDecoder, DeinterlaceApplicationStatus status)
    {
        var decoders = new DecoderRegistry(new());
        var native = new Decoder("native", nativeOrder);
        var capable = new Decoder("capable", VideoFieldOrder.TopFieldFirst, Capabilities);
        decoders.Register(native);
        decoders.Register(capable);

        using var reader = MediaReader.Open(_path, new MediaOptions(MediaMode.Video)
        {
            Deinterlace = s_fields with { Mode = mode },
        }, decoders);

        Assert.Multiple(() =>
        {
            Assert.That(reader.VideoInfo.CodecName, Is.EqualTo(usesCapableDecoder ? capable.Name : native.Name));
            Assert.That(reader.VideoInfo.DeinterlaceStatus, Is.EqualTo(status));
            Assert.That(native.Opens.Count, Is.EqualTo(mode == DeinterlaceMode.On ? 0 : 1));
            Assert.That(capable.Opens.Count, Is.EqualTo(usesCapableDecoder ? 1 : 0));
            if (mode == DeinterlaceMode.Auto && usesCapableDecoder)
                Assert.That(native.LastOpened!.IsDisposed, Is.True);
        });
    }

    [TestCase(DeinterlaceMode.On)]
    [TestCase(DeinterlaceMode.Auto)]
    public void KeepsTheNativeReaderWithAWarningWhenCapableDecodeFails(DeinterlaceMode mode)
    {
        var decoders = new DecoderRegistry(new());
        var native = new Decoder("native", VideoFieldOrder.BottomFieldFirst);
        decoders.Register(native);
        decoders.Register(new Decoder("capable", VideoFieldOrder.BottomFieldFirst, Capabilities) { CanOpen = false });

        using var reader = MediaReader.Open(_path, new MediaOptions(MediaMode.Video)
        {
            Deinterlace = s_fields with { Mode = mode },
        }, decoders);
        Assert.That(reader.VideoInfo.CodecName, Is.EqualTo(native.Name));
        Assert.That(reader.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.UnsupportedByBackend));
        Assert.That(reader.VideoInfo.DetectedFieldOrder, Is.EqualTo(VideoFieldOrder.BottomFieldFirst));
        Assert.That(native.LastOpened!.IsDisposed, Is.False);
        reader.Dispose();
        Assert.That(native.LastOpened.IsDisposed, Is.True);
    }

    [Test]
    public void AutoUsesCapableProgressiveDetectionWhenNativeScanOrderIsUnknown()
    {
        var decoders = new DecoderRegistry(new());
        var native = new Decoder("native", VideoFieldOrder.Unknown);
        decoders.Register(native);
        decoders.Register(new Decoder("capable", VideoFieldOrder.Progressive, Capabilities));

        using var reader = MediaReader.Open(_path, new MediaOptions(MediaMode.Video) { Deinterlace = s_fields }, decoders);
        Assert.That(reader.VideoInfo.CodecName, Is.EqualTo("capable"));
        Assert.That(reader.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.NotNeededProgressive));
        Assert.That(native.LastOpened!.IsDisposed, Is.True);
    }

    [Test]
    public void ForcedDeinterlacingFallsBackToNativeWhenTheCapableBackendIgnoresTheOptions()
    {
        var decoders = new DecoderRegistry(new());
        var native = new Decoder("native", VideoFieldOrder.TopFieldFirst);
        var oldWorker = new Decoder("old worker", VideoFieldOrder.TopFieldFirst, Capabilities) { IgnoreDeinterlacing = true };
        decoders.Register(native);
        decoders.Register(oldWorker);

        using var reader = MediaReader.Open(_path, new MediaOptions(MediaMode.Video)
        {
            Deinterlace = s_fields with { Mode = DeinterlaceMode.On },
        }, decoders);
        Assert.That(reader.VideoInfo.CodecName, Is.EqualTo(native.Name));
        Assert.That(reader.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.UnsupportedByBackend));
        Assert.That(oldWorker.LastOpened!.IsDisposed, Is.True);
    }

    [Test]
    public void ClipSettingsAffectOnlyItsVideoFrameGrid()
    {
        var decoders = new DecoderRegistry(new());
        var decoder = new Decoder("capable", VideoFieldOrder.TopFieldFirst, Capabilities);
        decoders.Register(decoder);
        var videoSource = VideoSource.Open(_path);
        var source = new SourceVideo { Source = { Value = videoSource } };
        var audio = new SourceSound { Source = { Value = SoundSource.Open(_path) } };
        var context = new CompositionContext(MediaTime.Zero) { Decoders = decoders };
        using var clip = source.ToResource(context);
        MediaTime duration = clip.Source!.Duration;
        var halfFrame = MediaTime.FromFrames(1, new Rational(60, 1));
        Assert.That(clip.Source.FrameRate, Is.EqualTo(new Rational(30, 1)));

        source.DeinterlaceMode.Value = DeinterlaceMode.Auto;
        source.DeinterlaceFrameRateMode.Value = DeinterlaceFrameRateMode.Field;
        bool bumped = false;
        clip.Reconcile(source, context, ref bumped);
        using var independent = videoSource.ToResource(context);
        Assert.Multiple(() =>
        {
            Assert.That(bumped, Is.True);
            Assert.That(clip.Source.FrameRate, Is.EqualTo(new Rational(60, 1)));
            Assert.That(clip.Source.GetFrameIndex(halfFrame), Is.EqualTo(1));
            Assert.That(clip.Source.GetFrameIndex(halfFrame + halfFrame), Is.EqualTo(2));
            Assert.That(clip.Source.Duration, Is.EqualTo(duration));
            Assert.That(clip.Source.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
            Assert.That(independent.FrameRate, Is.EqualTo(new Rational(30, 1)));
            Assert.That(source.TryGetOriginalDuration(out var videoDuration, decoders), Is.True);
            Assert.That(videoDuration, Is.EqualTo(duration));
            Assert.That(((ITimeMappedSource)source).GetSourceDuration(decoders), Is.EqualTo(duration));
            Assert.That(audio.TryGetOriginalDuration(out var audioDuration, decoders), Is.True);
            Assert.That(audioDuration, Is.EqualTo(duration));
            Assert.That(decoder.Opens.Last(), Is.EqualTo(new MediaOptions(MediaMode.Audio)));
        });
    }

    [Test]
    public void ThumbnailCacheTracksEffectiveSettingsAndInvalidatesOnEdit()
    {
        var source = new SourceVideo { Source = { Value = VideoSource.Open(_path) } };
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };
        var scene = new Scene();
        project.Items.Add(scene);
        var element = new Element();
        scene.Children.Add(element);
        element.Objects.Add(source);
        int invalidations = 0;
        source.ThumbnailsInvalidated += (_, _) => invalidations++;
        string? original = source.GetThumbnailsCacheKey();

        source.DeinterlaceFrameRateMode.Value = DeinterlaceFrameRateMode.Field;
        Assert.That(source.GetThumbnailsCacheKey(), Is.EqualTo(original));
        source.DeinterlaceMode.Value = DeinterlaceMode.Auto;
        var active = source.GetThumbnailsCacheKey();
        Assert.That(active, Is.Not.EqualTo(original));
        source.DeinterlaceMethod.Value = DeinterlaceMethod.Yadif;
        Assert.That(source.GetThumbnailsCacheKey(), Is.Not.EqualTo(active));
        active = source.GetThumbnailsCacheKey();
        source.DeinterlaceFieldOrder.Value = DeinterlaceFieldOrder.BottomFieldFirst;
        Assert.That(source.GetThumbnailsCacheKey(), Is.Not.EqualTo(active));
        Assert.That(invalidations, Is.EqualTo(4));
        element.Objects.Remove(source);
        source.DeinterlaceMode.Value = DeinterlaceMode.Off;
        Assert.That(source.GetThumbnailsCacheKey(), Is.EqualTo(original));
        Assert.That(invalidations, Is.EqualTo(4));
    }

    private sealed class Decoder(string name, VideoFieldOrder order, DecoderCapabilities capabilities = default) : IDecoderInfo
    {
        public string Name => name;
        public DecoderCapabilities Capabilities => capabilities;
        public List<MediaOptions> Opens { get; } = [];
        public MediaReader? LastOpened { get; private set; }
        public bool CanOpen { get; init; } = true;
        public bool IgnoreDeinterlacing { get; init; }
        public IEnumerable<string> VideoExtensions() => [".tmp"];
        public IEnumerable<string> AudioExtensions() => [".tmp"];

        public MediaReader? Open(string file, MediaOptions options)
        {
            Opens.Add(options);
            if (IgnoreDeinterlacing)
                options = options with { Deinterlace = default };
            return LastOpened = CanOpen ? new Reader(name, options, order, capabilities) : null;
        }
    }

    private sealed class Reader(string name, MediaOptions options, VideoFieldOrder order, DecoderCapabilities capabilities) : MediaReader
    {
        private bool Applied => options.Deinterlace.Mode != DeinterlaceMode.Off && capabilities.Supports(options.Deinterlace)
            && (options.Deinterlace.Mode == DeinterlaceMode.On || order != VideoFieldOrder.Progressive);
        private int Rate => Applied && options.Deinterlace.FrameRateMode == DeinterlaceFrameRateMode.Field ? 60 : 30;

        public override VideoStreamInfo VideoInfo => new(name, Rate * 4, new PixelSize(64, 32), new Rational(Rate))
        {
            DeinterlaceStatus = Applied ? DeinterlaceApplicationStatus.Applied
                : capabilities == DecoderCapabilities.None ? DeinterlaceApplicationStatus.Off : options.Deinterlace.GetFallbackStatus(order),
            DetectedFieldOrder = order,
        };

        public override AudioStreamInfo AudioInfo => new(name, new Rational(4), 48000, 2);
        public override bool HasVideo => options.StreamsToLoad.HasFlag(MediaMode.Video);
        public override bool HasAudio => options.StreamsToLoad.HasFlag(MediaMode.Audio);
        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image) { image = null; return false; }
        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound) { sound = null; return false; }
    }
}
