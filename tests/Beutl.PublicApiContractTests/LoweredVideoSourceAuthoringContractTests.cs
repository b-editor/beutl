using System.Diagnostics.CodeAnalysis;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Proves the lowered-paint drawing vocabulary is complete enough for an out-of-tree video-source node: the
/// same public entry point that paints shapes, text, bitmaps and image sources also paints a video frame.
/// </summary>
[TestFixture]
public sealed class LoweredVideoSourceAuthoringContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    private VideoSource? _source;
    private VideoSource.Resource? _resource;
    private Brush.Resource? _fill;

    [SetUp]
    public void SetUp()
    {
        TestVideoDecoder.EnsureRegistered();
        _source = new VideoSource();
        _source.ReadFrom(new Uri(TestVideoDecoder.CreateFile()));
        _resource = _source.ToResource(CompositionContext.Default);
        _fill = (Brush.Resource)new SolidColorBrush { Color = { CurrentValue = Colors.White } }
            .ToResource(CompositionContext.Default);
    }

    [TearDown]
    public void TearDown()
    {
        _fill?.Dispose();
        _fill = null;
        _resource?.Dispose();
        _resource = null;
        _source = null;
    }

    [Test]
    public void AnOutOfTreeNode_PaintsAVideoFrameUnderALoweredPaint()
    {
        using var node = new VideoPaintNode(_resource!, frame: 3, _fill!);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Bitmap bitmap = rasterization.Bitmap
                        ?? throw new AssertionException("The rasterization produced no bitmap.");
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[3]);
        Assert.That(alpha, Is.EqualTo(1f).Within(0.01f), "the decoded frame reached the target");
    }

    private sealed class VideoPaintNode(VideoSource.Resource source, int frame, Brush.Resource fill)
        : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<VideoSource.Resource> sourceResource = context.Borrow(
                source,
                source.RequireOriginal().Id,
                source.Version);

            context.Publish(context.PaintedSource(
                state: (s_bounds, frame),
                draw: static (session, state) => session.UseDeclaredResource<VideoSource.Resource>(
                    0,
                    currentSource => session.Canvas.DrawVideoSource(
                        currentSource,
                        state.frame,
                        session.Fill,
                        session.Pen)),
                fill: (fill, fill.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Custom(static _ => 1f, "video-native-density"),
                structuralKey: "out-of-tree-video-source",
                resources: [sourceResource]));
        }
    }

    private sealed class TestVideoDecoder : IDecoderInfo
    {
        private static bool s_registered;

        public string Name => "Contract Test Video Decoder";

        public static void EnsureRegistered()
        {
            if (s_registered)
                return;

            DecoderRegistry.Register(new TestVideoDecoder());
            s_registered = true;
        }

        public static string CreateFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "public-api-contract.contracttestvideo");
            if (!File.Exists(path))
                File.WriteAllBytes(path, []);

            return path;
        }

        public MediaReader? Open(string file, MediaOptions options)
            => IsSupported(file) ? new TestVideoReader() : null;

        public bool IsSupported(string file)
            => Path.GetExtension(file).Equals(".contracttestvideo", StringComparison.OrdinalIgnoreCase);

        public IEnumerable<string> VideoExtensions() => [".contracttestvideo"];

        public IEnumerable<string> AudioExtensions() => [];
    }

    private sealed class TestVideoReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new(
            "contract-test",
            30,
            new PixelSize(32, 24),
            new Rational(30, 1));

        public override AudioStreamInfo AudioInfo { get; } = new("contract-test", Rational.Zero, 44100, 2);

        public override bool HasVideo => true;

        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (frame is < 0 or >= 30)
            {
                image = null;
                return false;
            }

            var bitmap = new Bitmap(32, 24);
            bitmap.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(255, 255, 255, 255));
            image = Ref<Bitmap>.Create(bitmap);
            return true;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
