using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Hit-tests, measures or renders one filter effect over an ellipse, or over a full-bleed rectangle.
internal static class FilteredShapeHarness
{
    private const int DefaultSize = 100;

    public static bool HitTest(
        Rect content,
        FilterEffect effect,
        Point point,
        bool fullBleed = false,
        float outputScale = 1f,
        Rect? targetDomain = null,
        Brush.Resource? fill = null)
    {
        using RenderNode root = BuildTree(content, effect, fullBleed, fill);
        using var renderer = new RenderNodeRenderer(root, Options(outputScale, targetDomain));
        return renderer.HitTest(point);
    }

    /// <summary>The rectangle the request resolved for the stage - the extent it may answer for.</summary>
    public static Rect OutputBounds(Rect content, FilterEffect effect, bool fullBleed, Rect? targetDomain)
    {
        using RenderNode root = BuildTree(content, effect, fullBleed);
        using var renderer = new RenderNodeRenderer(root, Options(targetDomain: targetDomain));
        return renderer.Measure().OutputBounds;
    }

    public static Bitmap Render(
        Rect content,
        FilterEffect effect,
        bool fullBleed = false,
        int surface = DefaultSize,
        Rect? targetDomain = null,
        Brush.Resource? fill = null)
    {
        using RenderTarget target = RenderTarget.Create(surface, surface)
            ?? throw new InvalidOperationException("Could not allocate the resampling render target.");
        using var canvas = new ImmediateCanvas(
            target, RenderIntent.Preview, 1f, logicalSize: new Size(surface, surface));
        canvas.Clear(Colors.Transparent);

        using (RenderNode root = BuildTree(content, effect, fullBleed, fill))
        using (var renderer = new RenderNodeRenderer(root, Options(targetDomain: targetDomain)))
        {
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static RenderNode BuildTree(
        Rect content,
        FilterEffect effect,
        bool fullBleed,
        Brush.Resource? fill = null)
    {
        Brush.Resource brush = fill ?? Brushes.Resource.White;
        RenderNode leaf = fullBleed
            ? new RectangleRenderNode(content, brush, null)
            : new EllipseRenderNode(content, brush, null);

        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(leaf);
        return node;
    }

    private static RenderNodeRenderRequest Options(float outputScale = 1f, Rect? targetDomain = null)
        => new()
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = targetDomain ?? new Rect(0, 0, DefaultSize, DefaultSize),
            OutputScale = outputScale,
            MaxWorkingScale = outputScale,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        };
}
