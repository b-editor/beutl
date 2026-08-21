using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RendererIntentTests
{
    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void TheExecutedFrameRequestCarriesTheRendererIntent(RenderIntent intent)
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = CreateRenderer(intent);

            renderer.Render(CreateEmptyFrame());

            Assert.That(renderer.FrameRequestIntent, Is.EqualTo(intent),
                "The frame renderer issues every request, so it must carry the renderer's intent.");
        });
    }

    [Test]
    public void TheExecutedFrameRequestKeepsTheIntentWhenCacheOptionsRebuildTheFrameRenderer()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = CreateRenderer(RenderIntent.Delivery);

            renderer.CacheOptions = RenderCacheOptions.Disabled;
            renderer.ClearAllCaches();
            renderer.Render(CreateEmptyFrame());

            Assert.That(renderer.FrameRequestIntent, Is.EqualTo(RenderIntent.Delivery),
                "Rebuilding the frame renderer for new cache options must not reset its intent to the default.");
        });
    }

    // The canvas is the switch every brush-owned intermediate reads to choose degrade vs fail;
    // BrushIntermediateAllocationIntentTests covers the outcome itself.
    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void ThePaintingCanvasCarriesTheRendererIntent(RenderIntent intent)
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = CreateRenderer(intent);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.Intent, Is.EqualTo(intent));
                Assert.That(Renderer.GetInternalCanvas(renderer).Intent, Is.EqualTo(intent));
            });
        });
    }

    [Test]
    public void PreviewIsTheDefaultEvenWithoutAWorkingScaleCeiling()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                surface: new CpuRenderTarget(8, 8));

            Assert.Multiple(() =>
            {
                Assert.That(renderer.Intent, Is.EqualTo(RenderIntent.Preview));
                Assert.That(Renderer.GetInternalCanvas(renderer).Intent, Is.EqualTo(RenderIntent.Preview),
                    "An unbounded working scale must not promote a preview renderer to delivery fail-fast.");
            });
        });
    }

    [Test]
    public void UndefinedIntent_IsRejectedBeforeAnySurfaceIsCreated()
    {
        ArgumentOutOfRangeException? failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Renderer(8, 8, intent: (RenderIntent)12345));

        Assert.That(failure!.ParamName, Is.EqualTo("intent"));
    }

    [Test]
    public void UndefinedIntent_DisposesTheCallerSuppliedSurface()
    {
        var surface = new CpuRenderTarget(8, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            surface: surface,
            intent: (RenderIntent)12345));

        Assert.That(surface.IsDisposed, Is.True);
    }

    private static Renderer CreateRenderer(RenderIntent intent)
        => new(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            surface: new CpuRenderTarget(8, 8),
            intent: intent);

    private static CompositionFrame CreateEmptyFrame() => new(
        ImmutableArray<EngineObject.Resource>.Empty,
        new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        new PixelSize(8, 8),
        null);

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
