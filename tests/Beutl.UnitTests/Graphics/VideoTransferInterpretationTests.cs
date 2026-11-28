using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Graphics;

[TestFixture]
public sealed class VideoTransferInterpretationTests
{
    private static readonly PixelSize s_size = new(32, 16);

    [TestCase(BitmapColorTransfer.Bt709, VideoTransferInterpretation.QuickTime, 33, 139)]
    [TestCase(BitmapColorTransfer.Bt709, VideoTransferInterpretation.Bt1886, 18, 121)]
    [TestCase(BitmapColorTransfer.Bt709, VideoTransferInterpretation.Srgb, 30, 128)]
    [TestCase(BitmapColorTransfer.Bt709, VideoTransferInterpretation.Bt709Scene, 46, 140)]
    [TestCase(BitmapColorTransfer.Srgb, VideoTransferInterpretation.Bt1886, 18, 121)]
    [TestCase(BitmapColorTransfer.Unknown, VideoTransferInterpretation.QuickTime, 33, 139)]
    public void SourceVideosShareADecoderAndInterpretTheSameCodeValuesIndependently(
        BitmapColorTransfer transfer, VideoTransferInterpretation interpretation, int shadow, int midtone)
    {
        using var scope = ProxyTestScope.CreateWithoutProxy(s_size);
        var decoder = new PatchDecoder(transfer);
        CompositionContext context = CreateContext(decoder);
        var auto = CreateVideo(scope.OriginalPath, VideoTransferInterpretation.Auto);
        var overridden = CreateVideo(scope.OriginalPath, interpretation);
        using SourceVideo.Resource autoResource = auto.ToResource(context);
        using SourceVideo.Resource overriddenResource = overridden.ToResource(context);

        int[] autoPixels = Draw(autoResource);
        int[] overridePixels = Draw(overriddenResource);
        Assert.Multiple(() =>
        {
            Assert.That(decoder.OpenCount, Is.EqualTo(1));
            Assert.That(autoPixels, Is.EqualTo(transfer == BitmapColorTransfer.Bt709 ? new[] { 33, 139 } : [30, 128]).Within(1));
            Assert.That(overridePixels, Is.EqualTo(new[] { shadow, midtone }).Within(1));
            Assert.That(Draw(autoResource), Is.EqualTo(autoPixels), "Reinterpreting a frame must leave the shared bitmap untouched.");
        });
    }

    [Test]
    public void IccProfiledVideoKeepsItsCodeValuesAndPrimariesWhenOverridden()
    {
        using var scope = ProxyTestScope.CreateWithoutProxy(s_size);
        var iccSpace = BitmapColorSpace.CreateRgb(BitmapColorSpaceTransferFn.Srgb, BitmapColorSpaceXyz.Dcip3);
        CompositionContext context = CreateContext(new PatchDecoder(BitmapColorTransfer.Bt709, iccSpace));
        using var auto = CreateVideo(scope.OriginalPath, VideoTransferInterpretation.Auto).ToResource(context);
        using var explicitSrgb = CreateVideo(scope.OriginalPath, VideoTransferInterpretation.Srgb).ToResource(context);

        int[] expected = DrawIccPatches(auto);
        int[] overridden = DrawIccPatches(explicitSrgb);

        Assert.Multiple(() =>
        {
            Assert.That(expected[..3], Is.EqualTo(new[] { 128, 128, 128 }).Within(1));
            Assert.That(expected[5], Is.GreaterThan(160), "The Display P3 blue component should be converted for display.");
            Assert.That(overridden, Is.EqualTo(expected).Within(1));
            Assert.That(DrawIccPatches(auto), Is.EqualTo(expected), "The shared decoded frame must stay unchanged.");
        });
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public void ReopenedProxyRecoversIccCodeValues(bool savedColorSpace, bool wideGamut)
    {
        using var scope = ProxyTestScope.CreateWithoutProxy(s_size);
        string originalPath = Path.Combine(scope.RootPath, "icc-source.testvideo");
        const string proxyRelative = "proxy/icc.testvideo";
        string proxyPath = Path.GetFullPath(Path.Combine(scope.RootPath, proxyRelative));
        Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
        File.WriteAllBytes(originalPath, [1, 2, 3, 4]);
        File.WriteAllBytes(proxyPath, [1, 2, 3, 4]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(originalPath);
        BitmapColorSpace iccSpace = wideGamut
            ? BitmapColorSpace.CreateRgb(BitmapColorSpaceTransferFn.Srgb, BitmapColorSpaceXyz.Dcip3)
            : BitmapColorSpace.Srgb;
        var tags = new VideoColorTags(BitmapColorTransfer.Bt709, BitmapColorPrimaries.Bt709, VideoColorMatrix.Bt709, VideoColorRange.Full);
        DateTime now = DateTime.UtcNow;
        scope.Store.Register(new ProxyEntry(fingerprint, ProxyPreset.Quarter, ProxyState.Ready, proxyRelative, 4, s_size, s_size, now, now, null)
        {
            OriginalColorTags = tags,
            OriginalColorSpace = savedColorSpace ? iccSpace : null,
        });

        var decoder = new PatchDecoder(tags.Transfer, iccSpace, proxyPath);
        CompositionContext context = CreateContext(decoder);
        var video = CreateVideo(originalPath, VideoTransferInterpretation.Bt1886);
        int[] expected;
        using (var original = video.ToResource(context))
            expected = DrawIccPatches(original);

        // With stored metadata, the reopened proxy can recover the profile even with the original offline.
        if (savedColorSpace)
            File.Delete(originalPath);
        var reopenedStore = new ProxyStore(scope.RootPath);
        Assert.That(reopenedStore.TryGet(fingerprint, ProxyPreset.Quarter)?.OriginalColorSpace, Is.EqualTo(savedColorSpace ? iccSpace : null));
        context.Decoders!.ProxyResolver = new ProxyResolver(reopenedStore);

        using var proxy = video.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = context.Decoders, PreferProxy = true });
        Assert.That(proxy.Source!.ProxyResolution, Is.Not.Null);
        Assert.That(DrawIccPatches(proxy), Is.EqualTo(expected).Within(1));
    }

    [TestCase(BitmapColorTransfer.Pq)]
    [TestCase(BitmapColorTransfer.Hlg)]
    public void HdrUsesItsTaggedTransferRegardlessOfTheOverride(BitmapColorTransfer transfer)
    {
        using var scope = ProxyTestScope.CreateWithoutProxy(s_size);
        CompositionContext context = CreateContext(new PatchDecoder(transfer));
        using var auto = CreateVideo(scope.OriginalPath, VideoTransferInterpretation.Auto).ToResource(context);
        using var overridden = CreateVideo(scope.OriginalPath, VideoTransferInterpretation.Bt1886).ToResource(context);

        int[] baseline = Draw(auto);
        Assert.That(baseline[1], Is.GreaterThan(0));
        Assert.That(Draw(overridden), Is.EqualTo(baseline));
    }

    [Test]
    public void OfflinePlaceholderKeepsItsColors()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.testvideo");
        CompositionContext context = CreateContext(new PatchDecoder(BitmapColorTransfer.Unknown));
        using var auto = CreateVideo(missingPath, VideoTransferInterpretation.Auto).ToResource(context);
        using var overridden = CreateVideo(missingPath, VideoTransferInterpretation.Bt709Scene).ToResource(context);

        Assert.That(auto.Source!.IsOffline, Is.True);
        Assert.That(Draw(overridden), Is.EqualTo(Draw(auto)));
    }

    [Test]
    public void ChangingOnlyTheTransferInterpretationInvalidatesTheRenderNode()
    {
        using var scope = ProxyTestScope.CreateWithoutProxy(s_size);
        using var resource = VideoSource.Open(scope.OriginalPath).ToResource(CreateContext(new PatchDecoder(BitmapColorTransfer.Bt709)));
        var fill = Brushes.Resource.White;
        using var node = new VideoSourceRenderNode(resource, 0, fill, null);

        Assert.That(node.Update(resource, 0, fill, null), Is.False);
        Assert.That(node.Update(resource, 0, fill, null, VideoTransferInterpretation.Bt1886), Is.True);
        Assert.That(node.TransferInterpretation, Is.EqualTo(VideoTransferInterpretation.Bt1886));
        Assert.That(node.Update(resource, 0, fill, null, VideoTransferInterpretation.Bt1886), Is.False);
        Assert.That(node.Update(resource, 0, fill, null), Is.True);
    }

    [Test]
    public void EditingTheTransferCurveChangesTheThumbnailKeyAndInvalidatesTheStrip()
    {
        var video = new SourceVideo();
        var project = new Project();
        _ = new BeutlApplication { Projects = { project } };
        var scene = new Scene();
        project.Items.Add(scene);
        var element = new Element();
        scene.Children.Add(element);
        element.Objects.Add(video);
        string? autoKey = video.GetThumbnailsCacheKey();
        int invalidations = 0;
        video.ThumbnailsInvalidated += (_, _) => invalidations++;

        video.TransferInterpretation.Value = VideoTransferInterpretation.Bt1886;
        Assert.That(video.GetThumbnailsCacheKey(), Is.Not.EqualTo(autoKey));
        Assert.That(invalidations, Is.EqualTo(1));

        video.TransferInterpretation.Value = VideoTransferInterpretation.Auto;
        Assert.That(video.GetThumbnailsCacheKey(), Is.EqualTo(autoKey));
        Assert.That(invalidations, Is.EqualTo(2));

        element.Objects.Remove(video);
        video.TransferInterpretation.Value = VideoTransferInterpretation.Srgb;
        Assert.That(invalidations, Is.EqualTo(2));
    }

    private static CompositionContext CreateContext(IDecoderInfo decoder)
    {
        var decoders = new DecoderRegistry(new());
        decoders.Register(decoder);
        return new CompositionContext(MediaTime.Zero) { Decoders = decoders };
    }

    private static SourceVideo CreateVideo(string path, VideoTransferInterpretation interpretation) => new()
    {
        Source = { Value = VideoSource.Open(path) },
        TransferInterpretation = { Value = interpretation },
    };

    private static int[] Draw(SourceVideo.Resource video)
    {
        using Bitmap srgb = RenderSrgb(video);
        Span<Bgra8888> row = srgb.GetRow<Bgra8888>(srgb.Height / 2);
        return [row[srgb.Width / 4].G, row[srgb.Width * 3 / 4].G];
    }

    private static int[] DrawIccPatches(SourceVideo.Resource video)
    {
        using Bitmap srgb = RenderSrgb(video);
        Span<Bgra8888> row = srgb.GetRow<Bgra8888>(srgb.Height / 2);
        Bgra8888 gray = row[srgb.Width / 4];
        Bgra8888 color = row[srgb.Width * 3 / 4];
        return [gray.R, gray.G, gray.B, color.R, color.G, color.B];
    }

    private static Bitmap RenderSrgb(SourceVideo.Resource video)
    {
        using var target = new CpuRenderTarget(video.Source!.LogicalFrameSize);
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.DrawDrawable(video);
        }

        using Bitmap snapshot = target.Snapshot();
        return snapshot.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
    }

    private sealed class PatchDecoder(
        BitmapColorTransfer transfer, BitmapColorSpace? iccSpace = null, string? proxyPath = null) : IDecoderInfo
    {
        public string Name => "Video transfer test decoder";

        public int OpenCount { get; private set; }

        public IEnumerable<string> VideoExtensions() => [".testvideo"];

        public IEnumerable<string> AudioExtensions() => [];

        public MediaReader Open(string file, MediaOptions options)
        {
            OpenCount++;
            return new PatchReader(transfer, iccSpace, file == proxyPath);
        }
    }

    private sealed class PatchReader(BitmapColorTransfer transfer, BitmapColorSpace? iccSpace, bool isProxy) : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("test", 1L, s_size, new Rational(30, 1))
        {
            ColorTags = new VideoColorTags(transfer, BitmapColorPrimaries.Bt709, VideoColorMatrix.Bt709, VideoColorRange.Full),
            HasIccProfile = iccSpace != null && !isProxy,
        };

        private Ref<Bitmap>? _frame;

        public override AudioStreamInfo AudioInfo => throw new NotSupportedException();

        public override bool HasVideo => true;

        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = (_frame ??= Ref<Bitmap>.Create(CreateFrame())).Clone();
            return true;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            sound = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _frame?.Dispose();
            base.Dispose(disposing);
        }

        private Bitmap CreateFrame()
        {
            bool hdr = BitmapColorSpaceMapping.IsHdrTransfer(transfer);
            var primaries = VideoInfo.ColorTags.Primaries;
            BitmapColorSpace colorSpace = hdr
                ? BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, primaries)
                : BitmapColorSpaceMapping.BuildTargetColorSpace(transfer, primaries);
            var bitmap = new Bitmap(s_size.Width, s_size.Height, colorSpace: iccSpace ?? colorSpace);
            Bgra8888 left = iccSpace == null ? new(30, 30, 30, 255) : new(128, 128, 128, 255);
            Bgra8888 right = iccSpace == null ? new(128, 128, 128, 255) : new(64, 96, 160, 255);
            for (int y = 0; y < bitmap.Height; y++)
            {
                Span<Bgra8888> row = bitmap.GetRow<Bgra8888>(y);
                row[..(bitmap.Width / 2)].Fill(left);
                row[(bitmap.Width / 2)..].Fill(right);
            }

            if (!isProxy)
                return bitmap;

            using (bitmap)
                return bitmap.Convert(BitmapColorType.RgbaF16, colorSpace: colorSpace);
        }
    }
}
