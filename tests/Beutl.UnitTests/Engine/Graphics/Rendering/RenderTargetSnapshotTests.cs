using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// RenderTarget.SnapshotInto(Bitmap) reads the surface into an existing bitmap so repeat-snapshot
// callers (onion-skin compositing) can reuse one scratch bitmap instead of allocating a fresh
// video-frame-sized (LOH) bitmap per call. RenderTarget.Create needs Vulkan.
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
            using (var canvas = new ImmediateCanvas(target, 1f))
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

            using (var canvas = new ImmediateCanvas(target, 1f))
            {
                canvas.Clear(Colors.White);
            }

            target.SnapshotInto(scratch);
            Assert.That(IsWhite(scratch, 16, 16), Is.True, "first snapshot should read the white surface");

            using (var canvas = new ImmediateCanvas(target, 1f))
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
            // Correct size and pixel format, but sRGB instead of the surface's LinearSrgb: the raw
            // F16 bytes would be reinterpreted in the wrong gamma, so this must be rejected too.
            using var wrongColorSpace = new Bitmap(64, 48, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongColorSpace));
        });
    }

    [Test]
    public void RendererSnapshotIntoDestination_MatchesAllocatingSnapshot()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(64, 48);

            using Bitmap allocated = renderer.Snapshot();
            using Bitmap reused = NewScratch(allocated.Width, allocated.Height);
            renderer.SnapshotInto(reused);

            AssertRowsIdentical(allocated, reused, "Renderer allocating and reuse snapshot paths");
        });
    }

    // Compare every row's raw bytes (width * bytes-per-pixel) rather than sparsely sampling pixels:
    // sampling can pass while a stride/row-alignment bug silently corrupts the rest of the row.
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
}
