using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// RenderTarget.SnapshotInto(Bitmap) reads the surface into an existing bitmap so repeat-snapshot
// callers (onion-skin compositing) can reuse one scratch bitmap. RenderTarget.Create needs Vulkan.
[NonParallelizable]
[TestFixture]
public class RenderTargetSnapshotTests
{
    private static Bitmap NewScratch(int width, int height) =>
        new(width, height, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

    [Test]
    public void SnapshotIntoDestination_MatchesAllocatingSnapshot()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(16, 12, 32, 24), Brushes.Resource.White, null);
            }

            using Bitmap allocated = target.Snapshot();
            using Bitmap reused = NewScratch(64, 48);
            target.SnapshotInto(reused);

            // The reuse path must read back the exact same pixels as the allocating path.
            AssertRowsIdentical(allocated, reused, "allocating and reuse snapshot paths");
        });
    }

    [Test]
    public void SnapshotIntoDestination_OverwritesOnEachCall_AllowingReuse()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(32, 32)!;
            using Bitmap scratch = NewScratch(32, 32);

            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f))
            {
                canvas.Clear(Colors.White);
            }

            target.SnapshotInto(scratch);
            Assert.That(IsWhite(scratch, 16, 16), Is.True, "first snapshot should read the white surface");

            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f))
            {
                canvas.Clear(Colors.Black);
            }

            // Same scratch instance, second call must reflect the new (black) surface, not stale white.
            target.SnapshotInto(scratch);
            Assert.That(IsWhite(scratch, 16, 16), Is.False, "reused snapshot should be overwritten with the new surface");
        });
    }

    [Test]
    public void SnapshotIntoDestination_DimensionMismatch_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using Bitmap wrongSize = NewScratch(32, 32);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongSize));
        });
    }

    [Test]
    public void SnapshotIntoDestination_FormatMismatch_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            // Right size, wrong pixel format: ReadPixels would not convert, so this must be rejected.
            using var wrongFormat = new Bitmap(64, 48, BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongFormat));
        });
    }

    [Test]
    public void SnapshotIntoDestination_ColorSpaceMismatch_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            // sRGB instead of the surface's LinearSrgb (wrong gamma) must be rejected.
            using var wrongColorSpace = new Bitmap(64, 48, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongColorSpace));
        });
    }

    [Test]
    public void SnapshotIntoDestination_AlphaTypeMismatch_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            // Unpremul instead of the surface's Premul must be rejected. Isolates the AlphaType branch.
            using var wrongAlpha = new Bitmap(64, 48, BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, BitmapColorSpace.LinearSrgb);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongAlpha));
        });
    }

    [Test]
    public void SnapshotIntoDestination_ColorTypeMismatch_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            // Right size, AlphaType and ColorSpace, but Bgra8888 instead of the surface's RgbaF16.
            // Isolates the ColorType branch of the validation.
            using var wrongColorType = new Bitmap(64, 48, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongColorType));
        });
    }

    [Test]
    public void SnapshotIntoDestination_NullDestination_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;

            Assert.Throws<ArgumentNullException>(() => target.SnapshotInto(null!));
        });
    }

    [Test]
    public void CreateSnapshotBitmap_ProducesDestinationAcceptedBySnapshotInto()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using Bitmap scratch = target.CreateSnapshotBitmap();

            Assert.That(scratch.Width, Is.EqualTo(64));
            Assert.That(scratch.Height, Is.EqualTo(48));
            Assert.That(scratch.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(scratch.AlphaType, Is.EqualTo(BitmapAlphaType.Premul));
            Assert.That(scratch.ColorSpace.Equals(BitmapColorSpace.LinearSrgb), Is.True);

            // The factory's bitmap must satisfy SnapshotInto's format validation.
            Assert.DoesNotThrow(() => target.SnapshotInto(scratch));
        });
    }

    [Test]
    public void RendererSnapshotIntoDestination_MatchesAllocatingSnapshot()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(64, 48, RenderIntent.Delivery);

            using Bitmap allocated = renderer.Snapshot();
            using Bitmap reused = NewScratch(allocated.Width, allocated.Height);
            renderer.SnapshotInto(reused);

            AssertRowsIdentical(allocated, reused, "Renderer allocating and reuse snapshot paths");
        });
    }

    // The IRenderer.SnapshotInto default (for implementors that don't override it) must produce the
    // same pixels as Snapshot(). CPU-only: no Vulkan.
    [Test]
    public void IRendererDefaultSnapshotInto_FallsBackToSnapshotAndCopies()
    {
        using Bitmap content = NewScratch(40, 24);
        using (var canvas = new SKCanvas(content.SKBitmap))
        {
            canvas.Clear(new SKColor(20, 40, 60));
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(SKRect.Create(8, 4, 16, 10), paint);
        }

        IRenderer renderer = new FakeSnapshotRenderer(content);
        using Bitmap expected = renderer.Snapshot();
        using Bitmap actual = NewScratch(40, 24);

        renderer.SnapshotInto(actual);

        AssertRowsIdentical(expected, actual, "IRenderer default SnapshotInto vs Snapshot");
    }

    [Test]
    public void IRendererDefaultSnapshotInto_DimensionMismatch_Throws()
    {
        using Bitmap content = NewScratch(40, 24);
        IRenderer renderer = new FakeSnapshotRenderer(content);
        using Bitmap wrongSize = NewScratch(20, 20);

        Assert.Throws<ArgumentException>(() => renderer.SnapshotInto(wrongSize));
    }

    // Same bytes-per-pixel but a different color space. CopyFrom only checks bytes-per-pixel, so the
    // default must reject this rather than raw-copy linear bytes as sRGB.
    [Test]
    public void IRendererDefaultSnapshotInto_FormatMismatch_Throws()
    {
        using Bitmap content = NewScratch(40, 24);
        IRenderer renderer = new FakeSnapshotRenderer(content);
        using var wrongFormat = new Bitmap(40, 24, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

        Assert.Throws<ArgumentException>(() => renderer.SnapshotInto(wrongFormat));
    }

    // Compare every row's raw bytes rather than sampling pixels, to catch stride/row-alignment bugs.
    private static void AssertRowsIdentical(Bitmap expected, Bitmap actual, string paths)
    {
        Assert.That(actual.Width, Is.EqualTo(expected.Width));
        Assert.That(actual.Height, Is.EqualTo(expected.Height));
        for (int y = 0; y < expected.Height; y++)
        {
            Assert.That(actual.GetRow(y).SequenceEqual(expected.GetRow(y)), Is.True,
                $"row {y} differs between {paths}");
        }
    }

    private static bool IsWhite(Bitmap bmp, int x, int y)
    {
        var p = bmp.SKBitmap.GetPixel(x, y);
        return p.Red > 150 && p.Green > 150 && p.Blue > 150;
    }

    // Minimal IRenderer that does NOT override SnapshotInto, so calls route through the interface
    // default. Snapshot() clones the supplied content; unused members throw.
    private sealed class FakeSnapshotRenderer(Bitmap content) : IRenderer
    {
        public PixelSize FrameSize => new(content.Width, content.Height);

        public TimeSpan Time => default;

        public bool IsDisposed => false;

        public bool IsGraphicsRendering => false;

        public RenderCacheOptions CacheOptions { get; set; } = RenderCacheOptions.Default;

        public Bitmap Snapshot() => content.Clone();

        public void Render(CompositionFrame frame) => throw new NotSupportedException();

        public Drawable? HitTest(CompositionFrame frame, Point point) => throw new NotSupportedException();

        public void UpdateFrame(CompositionFrame frame) => throw new NotSupportedException();

        public Rect[] GetBoundaries(int zIndex) => throw new NotSupportedException();

        public DrawableRenderNode? FindRenderNode(Drawable drawable) => throw new NotSupportedException();

        public void Dispose() { }
    }
}
