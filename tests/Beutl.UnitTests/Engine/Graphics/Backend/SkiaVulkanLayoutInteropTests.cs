using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
[NonParallelizable]
public class SkiaVulkanLayoutInteropTests
{
    [Test]
    public void SnapshotThenBackendCopyThenSkiaDraw_PreservesContentsAcrossRepeatedHandoffs()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        if (context.Backend != GraphicsBackend.Vulkan)
            Assert.Ignore("This test exercises the shared Vulkan mutable state, not Metal interop.");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create(4, 4)!;
            using RenderTarget destination = RenderTarget.Create(4, 4)!;
            foreach (SKColor color in new[] { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Red })
            {
                source.BeginDraw();
                source.Value.Canvas.Clear(color);
                // Snapshot changes the original image's layout while making its GPU copy. Reusing
                // the pre-Snapshot layout at the next backend hand-off triggers issue #2263.
                using SKImage snapshot = source.RawValue.Snapshot();
                source.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                context.CopyTexture(source.Texture!, destination.Texture!);

                destination.BeginDraw();
                using var paint = new SKPaint { Color = SKColors.White };
                destination.Value.Canvas.DrawRect(SKRect.Create(0, 0, 1, 1), paint);
                using Bitmap actual = destination.Snapshot();
                using Bitmap expected = source.Snapshot();
                Assert.Multiple(() =>
                {
                    Assert.That(actual.GetPixelSpan<RgbaF16>()[0], Is.EqualTo(new RgbaF16((Half)1, (Half)1, (Half)1, (Half)1)));
                    Assert.That(actual.GetPixelSpan<RgbaF16>()[15], Is.EqualTo(expected.GetPixelSpan<RgbaF16>()[15]));
                });
            }
        });
    }

    [Test]
    public void RecreatedSurfaces_ShareTheTexturesLatestLayout()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        if (context.Backend != GraphicsBackend.Vulkan)
            Assert.Ignore("This test exercises the shared Vulkan mutable state, not Metal interop.");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ITexture2D texture = context.CreateTexture2D(4, 4, TextureFormat.RGBA8Unorm);
            foreach (byte value in new byte[] { 40, 160, 240 })
            {
                byte[] pixels = Enumerable.Repeat(new byte[] { value, 0, 0, 255 }, 16).SelectMany(x => x).ToArray();
                texture.Upload(pixels);
                texture.PrepareForSkiaSampling(requireCompletion: false);
                using SKSurface surface = texture.CreateSkiaSurface();
                using SKImage snapshot = surface.Snapshot();
                using var actual = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()));
                Assert.That(snapshot.ReadPixels(actual.Info, actual.GetPixels()), Is.True);
                Assert.That(actual.Pixels, Is.All.EqualTo(new SKColor(value, 0, 0, 255)));
            }
        });
    }
}
