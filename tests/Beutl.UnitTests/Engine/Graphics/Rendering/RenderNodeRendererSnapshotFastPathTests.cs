using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderNodeRendererSnapshotFastPathTests
{
    [Test]
    public void TakeRasterizationBitmap_FullExtentTransfersOriginalSnapshot()
    {
        Bitmap complete = CreateTokenBitmap(4, 2);

        Bitmap selected = RenderNodeRenderer.TakeRasterizationBitmap(
            complete,
            new PixelRect(0, 0, 4, 2));

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.SameAs(complete));
            Assert.That(complete.IsDisposed, Is.False);
            AssertToken(selected, 3, 1, 7);
        });

        selected.Dispose();
        selected.Dispose();
        Assert.That(complete.IsDisposed, Is.True);
    }

    [Test]
    public void TakeRasterizationBitmap_PartialExtentCopiesPixelsAndDisposesOriginal()
    {
        Bitmap complete = CreateTokenBitmap(4, 3);

        using Bitmap selected = RenderNodeRenderer.TakeRasterizationBitmap(
            complete,
            new PixelRect(1, 1, 2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.Not.SameAs(complete));
            Assert.That(complete.IsDisposed, Is.True);
            Assert.That(selected.Width, Is.EqualTo(2));
            Assert.That(selected.Height, Is.EqualTo(2));
            AssertToken(selected, 0, 0, 5);
            AssertToken(selected, 1, 0, 6);
            AssertToken(selected, 0, 1, 9);
            AssertToken(selected, 1, 1, 10);
        });
    }

    [Test]
    public void TakeRasterizationBitmap_CropFailureDisposesOriginal()
    {
        Bitmap complete = CreateTokenBitmap(4, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RenderNodeRenderer.TakeRasterizationBitmap(
                complete,
                new PixelRect(3, 0, 2, 2)));

        Assert.That(complete.IsDisposed, Is.True);
    }

    [Test]
    public void Rasterize_FullOutputPreservesPixelsAndOwnsBitmapIndependently()
    {
        var bounds = new Rect(0, 0, 4, 2);
        using var source = new CpuRenderTarget(4, 2);
        DrawColumnPattern(source);
        using var node = new MaterializedSourceNode(source, bounds, requireFullReadback: false);
        var factory = new TrackingTargetFactory();
        var renderer = CreateRenderer(node, bounds, requestedRegion: null, factory);
        RenderNodeRasterization? rasterization = null;

        try
        {
            rasterization = renderer.Rasterize();
            Bitmap bitmap = rasterization.Bitmap
                ?? throw new AssertionException("The full-output fixture must produce a bitmap.");

            Assert.Multiple(() =>
            {
                Assert.That(rasterization.Bounds, Is.EqualTo(bounds));
                Assert.That(bitmap.Width, Is.EqualTo(4));
                Assert.That(bitmap.Height, Is.EqualTo(2));
                AssertDominant(bitmap, 0, 0, red: true, green: false, blue: false);
                AssertDominant(bitmap, 3, 1, red: true, green: true, blue: true);
            });

            renderer.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(bitmap.IsDisposed, Is.False);
                AssertDominant(bitmap, 2, 0, red: false, green: false, blue: true);
                Assert.That(factory.Targets, Is.Not.Empty);
                Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(
                    target => target.IsDisposed && target.DisposeCalls == 1));
                Assert.That(source.IsDisposed, Is.False);
            });

            rasterization.Dispose();
            rasterization.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(rasterization.IsDisposed, Is.True);
                Assert.That(bitmap.IsDisposed, Is.True);
                Assert.Throws<ObjectDisposedException>(() => _ = rasterization.Bitmap);
            });
        }
        finally
        {
            rasterization?.Dispose();
            renderer.Dispose();
        }
    }

    [Test]
    public void Rasterize_PartialOutputCropsExpandedSnapshot()
    {
        var bounds = new Rect(0, 0, 4, 2);
        var requestedRegion = new Rect(1, 0, 2, 2);
        using var source = new CpuRenderTarget(4, 2);
        DrawColumnPattern(source);
        using var node = new MaterializedSourceNode(source, bounds, requireFullReadback: true);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(node, bounds, requestedRegion, factory);

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The partial-output fixture must produce a bitmap.");

        Assert.Multiple(() =>
        {
            Assert.That(node.ReadbackSize, Is.EqualTo(new PixelSize(4, 2)),
                "The target readback must keep the execution snapshot larger than the selected subset.");
            Assert.That(rasterization.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(bitmap.Width, Is.EqualTo(2));
            Assert.That(bitmap.Height, Is.EqualTo(2));
            AssertDominant(bitmap, 0, 0, red: false, green: true, blue: false);
            AssertDominant(bitmap, 1, 1, red: false, green: false, blue: true);
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        Rect targetDomain,
        Rect? requestedRegion,
        IRenderTargetFactory targetFactory)
        => new(node, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = targetDomain,
                RequestedRegion = requestedRegion,
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
            TargetFactory = targetFactory,
        });

    private static Bitmap CreateTokenBitmap(int width, int height)
    {
        var bitmap = new Bitmap(
            width,
            height,
            BitmapColorType.Rgba8888,
            BitmapAlphaType.Unpremul);
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < width; x++)
            {
                byte token = checked((byte)((y * width) + x));
                int offset = x * bitmap.BytesPerPixel;
                row[offset] = token;
                row[offset + 1] = (byte)(token + 1);
                row[offset + 2] = (byte)(token + 2);
                row[offset + 3] = byte.MaxValue;
            }
        }

        return bitmap;
    }

    private static void AssertToken(Bitmap bitmap, int x, int y, byte expected)
    {
        Span<byte> row = bitmap.GetRow(y);
        int offset = x * bitmap.BytesPerPixel;
        Assert.That(row[offset], Is.EqualTo(expected));
    }

    private static void DrawColumnPattern(RenderTarget target)
    {
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        SKColor[] colors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.White];
        using var paint = new SKPaint();
        for (int x = 0; x < colors.Length; x++)
        {
            paint.Color = colors[x];
            canvas.DrawRect(x, 0, 1, target.Height, paint);
        }

        canvas.Flush();
    }

    private static void AssertDominant(
        Bitmap bitmap,
        int x,
        int y,
        bool red,
        bool green,
        bool blue)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        int offset = x * 4;
        float actualRed = (float)BitConverter.UInt16BitsToHalf(row[offset]);
        float actualGreen = (float)BitConverter.UInt16BitsToHalf(row[offset + 1]);
        float actualBlue = (float)BitConverter.UInt16BitsToHalf(row[offset + 2]);
        const float threshold = 0.75f;

        Assert.Multiple(() =>
        {
            Assert.That(actualRed, red ? Is.GreaterThan(threshold) : Is.LessThan(0.25f));
            Assert.That(actualGreen, green ? Is.GreaterThan(threshold) : Is.LessThan(0.25f));
            Assert.That(actualBlue, blue ? Is.GreaterThan(threshold) : Is.LessThan(0.25f));
        });
    }

    private sealed class MaterializedSourceNode(
        RenderTarget source,
        Rect bounds,
        bool requireFullReadback) : RenderNode
    {
        public PixelSize? ReadbackSize { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> target = context.Borrow(
                source,
                "snapshot-fast-path-source",
                version: 1);
            context.Publish(context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    target,
                    bounds,
                    EffectiveScale.At(1),
                    PixelRect.FromRect(bounds, 1),
                    default,
                    RenderHitTestContract.OutputBounds)));

            if (!requireFullReadback)
                return;

            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    session => session.UseSnapshot(
                        bitmap => ReadbackSize = new PixelSize(bitmap.Width, bitmap.Height)),
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.Readback,
                    runtimeIdentity: new RenderRuntimeIdentity("snapshot-fast-path-readback"))));
        }
    }

    private sealed class TrackingTargetFactory : IRenderTargetFactory
    {
        public List<TrackingRenderTarget> Targets { get; } = [];

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            var target = new TrackingRenderTarget(deviceSize.Width, deviceSize.Height);
            Targets.Add(target);
            return target;
        }
    }

    private sealed class TrackingRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
                DisposeCalls++;

            base.Dispose(disposing);
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height);

    private static SKSurface CreateSurface(int width, int height)
        => SKSurface.Create(new SKImageInfo(
               width,
               height,
               SKColorType.RgbaF16,
               SKAlphaType.Premul,
               SKColorSpace.CreateSrgbLinear()))
           ?? throw new InvalidOperationException("Could not create a CPU render target.");
}
