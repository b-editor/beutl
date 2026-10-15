using Beutl.Graphics;
using Beutl.Graphics.Rendering;
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
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(16, 12, 32, 24), Brushes.Resource.White, null);
            }

            using Bitmap allocated = target.Snapshot();
            using Bitmap reused = NewScratch(64, 48);
            target.SnapshotInto(reused);

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

            using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f))
            {
                canvas.Clear(Colors.White);
            }

            target.SnapshotInto(scratch);
            Assert.That(IsWhite(scratch, 16, 16), Is.True, "first snapshot should read the white surface");

            using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f))
            {
                canvas.Clear(Colors.Black);
            }

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

    // Each destination differs from the surface's RgbaF16, Premul, linear-sRGB format in one property only, so
    // each case isolates one branch of the validation.
    [TestCase(BitmapColorType.RgbaF16, BitmapAlphaType.Premul, false, TestName = "SnapshotIntoDestination_ColorSpaceMismatch_Throws")]
    [TestCase(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, true, TestName = "SnapshotIntoDestination_AlphaTypeMismatch_Throws")]
    [TestCase(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, true, TestName = "SnapshotIntoDestination_ColorTypeMismatch_Throws")]
    public void SnapshotIntoDestination_FormatMismatch_Throws(
        BitmapColorType colorType,
        BitmapAlphaType alphaType,
        bool linearColorSpace)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var wrongFormat = new Bitmap(
                64,
                48,
                colorType,
                alphaType,
                linearColorSpace ? BitmapColorSpace.LinearSrgb : BitmapColorSpace.Srgb);

            Assert.Throws<ArgumentException>(() => target.SnapshotInto(wrongFormat));
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
            using var renderer = new Renderer(64, 48, RenderIntent.Preview);

            using Bitmap allocated = renderer.Snapshot();
            using Bitmap reused = NewScratch(allocated.Width, allocated.Height);
            renderer.SnapshotInto(reused);

            AssertRowsIdentical(allocated, reused, "Renderer allocating and reuse snapshot paths");
        });
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
}
