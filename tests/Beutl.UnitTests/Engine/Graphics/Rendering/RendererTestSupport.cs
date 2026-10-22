using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Builds the CPU-backed renderers and the frames the Renderer tests draw.
internal static class RendererTestSupport
{
    /// <summary>A preview renderer at output scale 1 that draws into a CPU surface instead of a GPU one.</summary>
    public static Renderer CreateCpuRenderer(
        int width,
        int height,
        RenderIntent intent = RenderIntent.Preview,
        float maxWorkingScale = float.PositiveInfinity)
        => new(
            width,
            height,
            intent,
            renderScale: 1,
            maxWorkingScale: maxWorkingScale,
            surface: new CpuRenderTarget(width, height));

    /// <summary>A one-second frame of <paramref name="drawables"/> with no eligibility snapshot.</summary>
    public static CompositionFrame CreateFrame(PixelSize size, params Drawable[] drawables)
        => new(
            [.. drawables.Select(static drawable =>
                (EngineObject.Resource)drawable.ToResource(CompositionContext.Default))],
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)),
            size,
            null);

    /// <summary>
    /// Updates a CPU renderer with a frame of <paramref name="drawables"/> on the render thread and returns what
    /// <paramref name="query"/> reads from it.
    /// </summary>
    /// <remarks>
    /// The nodes are disposed with the renderer, so the query walks them before it returns and only their
    /// identities can be compared afterwards.
    /// </remarks>
    public static T QueryFrame<T>(int width, int height, Func<Renderer, T> query, params Drawable[] drawables)
        => RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = CreateCpuRenderer(width, height);
            renderer.UpdateFrame(CreateFrame(new PixelSize(width, height), drawables));
            return query(renderer);
        });
}
