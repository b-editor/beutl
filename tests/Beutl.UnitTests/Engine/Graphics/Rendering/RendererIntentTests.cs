using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Threading;
using static Beutl.UnitTests.Engine.Graphics.Rendering.RendererTestSupport;

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
            using Renderer renderer = CreateCpuRenderer(8, 8, intent, maxWorkingScale: 1);

            renderer.Render(CreateFrame(new(8, 8)));

            Assert.That(renderer.FrameRequestIntent, Is.EqualTo(intent),
                "The frame renderer issues every request, so it must carry the renderer's intent.");
        });
    }

    [Test]
    public void TheExecutedFrameRequestKeepsTheIntentWhenCacheOptionsRebuildTheFrameRenderer()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = CreateCpuRenderer(8, 8, RenderIntent.Delivery, maxWorkingScale: 1);

            renderer.CacheOptions = RenderCacheOptions.Disabled;
            renderer.ClearAllCaches();
            renderer.Render(CreateFrame(new(8, 8)));

            Assert.That(renderer.FrameRequestIntent, Is.EqualTo(RenderIntent.Delivery),
                "Rebuilding the frame renderer for new cache options must not reset its intent to the default.");
        });
    }

    // The canvas is the switch every brush-owned intermediate reads to choose degrade vs fail;
    // BrushIntermediateAllocationIntentTests covers the outcome itself.
    [TestCase(RenderIntent.Preview, 1f)]
    [TestCase(RenderIntent.Delivery, 1f)]
    [TestCase(RenderIntent.Preview, float.PositiveInfinity)]
    public void ThePaintingCanvasCarriesTheRendererIntent(RenderIntent intent, float maxWorkingScale)
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = CreateCpuRenderer(8, 8, intent, maxWorkingScale);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.Intent, Is.EqualTo(intent));
                Assert.That(Renderer.GetInternalCanvas(renderer).Intent, Is.EqualTo(intent));
            });
        });
    }

    [Test]
    public void UndefinedIntent_IsRejectedBeforeAnySurfaceIsCreated()
    {
        ArgumentOutOfRangeException? failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Renderer(8, 8, (RenderIntent)12345));

        Assert.That(failure!.ParamName, Is.EqualTo("intent"));
    }

    [Test]
    public void UndefinedIntent_DisposesTheCallerSuppliedSurface()
    {
        var surface = new CpuRenderTarget(8, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Renderer(
            width: 8,
            height: 8,
            (RenderIntent)12345,
            renderScale: 1,
            maxWorkingScale: 1,
            surface: surface));

        Assert.That(surface.IsDisposed, Is.True);
    }
}
