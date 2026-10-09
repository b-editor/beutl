using System.Reflection;

using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;

using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// b-editor/beutl#2705: a blit of a GPU render target must not copy the whole texture.
[TestFixture]
[NonParallelizable]
public class SkiaSurfaceSamplingTests
{
    private const int Size = 256;

    [Test]
    public void DrawSurface_SamplesAVulkanTargetInPlace()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        if (context.Backend != GraphicsBackend.Vulkan)
            Assert.Ignore("Skia renders through Metal on the composite backend, which already wraps textures.");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreateTarget();
            using RenderTarget destination = CreateTarget();
            Write(source);

            destination.BeginDraw();
            source.PrepareBackendForSkiaSampling();
            destination.Value.Canvas.DrawSurface(source.Value, 0, 0);
            context.SkiaContext.Flush(submit: true, synchronous: true);

            var texture = (GRBackendTexture)typeof(VulkanTexture2D).GetField(
                "_skiaBackendTexture", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue((VulkanTexture2D)source.Texture!)!;
            // Sampled in place, the image is left ready for shader reads. Wrapped as a render target, which Skia
            // cannot sample, it was copied first and left as a transfer source.
            Assert.That(SkiaVulkanInterop.GetImageLayout(texture), Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal));
        });
    }

    [Test]
    public void ReleasingASnapshot_SkipsTheCopySkiaSchedulesForIt()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GRContext skia = context.SkiaContext;
            using RenderTarget source = CreateTarget();
            using RenderTarget destination = CreateTarget();
            long textureBytes = (long)Size * Size * 8;

            long released = BudgetGrowth(skia, source, destination, release: true);
            long kept = BudgetGrowth(skia, source, destination, release: false);

            Assert.Multiple(() =>
            {
                Assert.That(kept, Is.GreaterThanOrEqualTo(textureBytes),
                    "A snapshot the surface keeps across the flush must be copied, or this test observes nothing.");
                Assert.That(released, Is.LessThan(textureBytes));
            });
        });
    }

    [Test]
    public void ReleasedSnapshot_KeepsWhatWasDrawnWhenTheSourceIsOverwrittenBeforeTheFlush()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreateTarget();
            using RenderTarget destination = CreateTarget();
            source.BeginDraw();
            source.Value.Canvas.Clear(SKColors.Red);

            destination.BeginDraw();
            source.PrepareBackendForSkiaSampling();
            using (SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(source.Value))
                destination.Value.Canvas.DrawImage(snapshot.Image, 0, 0, SKSamplingOptions.Default, null);

            // With the copy skipped, the recorded draw reads the source itself, so it must still run before this.
            source.BeginDraw();
            source.Value.Canvas.Clear(SKColors.Blue);
            context.SkiaContext.Flush(submit: true, synchronous: true);

            Assert.Multiple(() =>
            {
                Assert.That(CenterPixel(destination), Is.EqualTo(SKColors.Red));
                Assert.That(CenterPixel(source), Is.EqualTo(SKColors.Blue), "The source was not overwritten.");
            });
        });
    }

    private static SKColor CenterPixel(RenderTarget target)
    {
        using var pixel = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.That(target.Value.ReadPixels(pixel.Info, pixel.GetPixels(), pixel.RowBytes, Size / 2, Size / 2), Is.True);
        return pixel.GetPixel(0, 0);
    }

    private static long BudgetGrowth(GRContext skia, RenderTarget source, RenderTarget destination, bool release)
    {
        Write(source);
        skia.Flush(submit: true, synchronous: true);
        skia.PurgeUnlockedResources(scratchResourcesOnly: false);
        skia.GetResourceCacheUsage(out _, out long before);

        destination.BeginDraw();
        source.PrepareBackendForSkiaSampling();
        if (release)
        {
            using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(source.Value);
            destination.Value.Canvas.DrawImage(snapshot.Image, 0, 0, SKSamplingOptions.Default, null);
        }
        else
        {
            using SKImage image = source.Value.Snapshot();
            destination.Value.Canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default, null);
        }
        skia.Flush(submit: true, synchronous: true);

        skia.GetResourceCacheUsage(out _, out long after);
        return after - before;
    }

    private static RenderTarget CreateTarget()
        => RenderTarget.Create(Size, Size) ?? throw new InvalidOperationException("RenderTarget.Create returned null.");

    private static void Write(RenderTarget target)
    {
        target.BeginDraw();
        using var paint = new SKPaint { Color = SKColors.Red };
        target.Value.Canvas.DrawRect(SKRect.Create(0, 0, Size / 2, Size / 2), paint);
    }
}
