using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// 実 Vulkan テクスチャを介した <see cref="ImmediateCanvas"/> の描画テスト。
/// <see cref="RenderTarget.CreateNull"/> ではなく <see cref="RenderTarget.Create"/> を使うため、
/// Vulkan が利用できない環境ではスキップする。
/// </summary>
[NonParallelizable]
public class ImmediateCanvasVulkanTests
{
    [Test]
    public void Clear_FillsSurfaceWithSolidColor()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(8, 8);
            Assert.That(target, Is.Not.Null);

            using (var canvas = new ImmediateCanvas(target!, RenderIntent.Delivery))
            {
                canvas.Clear(Colors.Red);
            }

            using var snapshot = target!.Snapshot();
            AssertEveryPixelMatches(snapshot, expectedRed: 1f, expectedGreen: 0f, expectedBlue: 0f);
        });
    }

    [Test]
    public void DrawRectangle_FillsRegionWithBrushColor()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(16, 16);
            Assert.That(target, Is.Not.Null);

            using (var canvas = new ImmediateCanvas(target!, RenderIntent.Delivery))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(0, 0, 16, 16), Brushes.Resource.Lime, pen: null);
            }

            using var snapshot = target!.Snapshot();
            AssertEveryPixelMatches(snapshot, expectedRed: 0f, expectedGreen: 1f, expectedBlue: 0f);
        });
    }

    [Test]
    public void Clear_OnSubregion_DoesNotAffectClippedAway()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(16, 16);
            Assert.That(target, Is.Not.Null);

            using (var canvas = new ImmediateCanvas(target!, RenderIntent.Delivery))
            {
                canvas.Clear(Colors.Black);
                using (canvas.PushClip(new Rect(0, 0, 8, 16)))
                {
                    canvas.Clear(Colors.White);
                }
            }

            using var snapshot = target!.Snapshot();
            // Linear sRGB: white -> 1, black -> 0
            AssertHalfPixelMatches(snapshot, x: 2, y: 8, expectedRed: 1f, expectedGreen: 1f, expectedBlue: 1f);
            AssertHalfPixelMatches(snapshot, x: 12, y: 8, expectedRed: 0f, expectedGreen: 0f, expectedBlue: 0f);
        });
    }

    private static void AssertEveryPixelMatches(Bitmap snapshot, float expectedRed, float expectedGreen, float expectedBlue)
    {
        // RgbaF16 は 4ch×2byte のハーフフロート。SnapshotはLinearSrgb。
        Assert.That(snapshot.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        Assert.That(snapshot.ColorSpace, Is.EqualTo(BitmapColorSpace.LinearSrgb));

        var span = snapshot.GetPixelSpan();
        for (int i = 0; i < span.Length; i += 8)
        {
            float r = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(span.Slice(i, 2)));
            float g = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(span.Slice(i + 2, 2)));
            float b = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(span.Slice(i + 4, 2)));

            Assert.That(r, Is.EqualTo(expectedRed).Within(0.01f), $"R mismatch at byte offset {i}");
            Assert.That(g, Is.EqualTo(expectedGreen).Within(0.01f), $"G mismatch at byte offset {i}");
            Assert.That(b, Is.EqualTo(expectedBlue).Within(0.01f), $"B mismatch at byte offset {i}");
        }
    }

    private static void AssertHalfPixelMatches(Bitmap snapshot, int x, int y, float expectedRed, float expectedGreen, float expectedBlue)
    {
        int rowBytes = snapshot.RowBytes;
        var row = snapshot.GetRow(y);
        int offset = x * 8;
        float r = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row.Slice(offset, 2)));
        float g = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row.Slice(offset + 2, 2)));
        float b = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row.Slice(offset + 4, 2)));

        Assert.That(r, Is.EqualTo(expectedRed).Within(0.01f), $"R mismatch at ({x},{y})");
        Assert.That(g, Is.EqualTo(expectedGreen).Within(0.01f), $"G mismatch at ({x},{y})");
        Assert.That(b, Is.EqualTo(expectedBlue).Within(0.01f), $"B mismatch at ({x},{y})");
        _ = rowBytes;
    }
}
