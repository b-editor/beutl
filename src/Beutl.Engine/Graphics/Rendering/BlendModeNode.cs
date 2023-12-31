using Beutl.Media.Source;
using Beutl.Rendering.Cache;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class BlendModeNode(BlendMode blendMode) : ContainerNode, ISupportRenderCache
{
    public BlendMode BlendMode { get; } = blendMode;

    public bool Equals(BlendMode blendMode)
    {
        return BlendMode == blendMode;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushBlendMode(BlendMode))
        {
            base.Render(canvas);
        }
    }

    void ISupportRenderCache.Accepts(RenderCache cache)
    {
        if (BlendMode == BlendMode.SrcOver)
        {
            cache.IncrementRenderCount();
        }
        else
        {
            cache.ReportRenderCount(0);
        }
    }

    void ISupportRenderCache.RenderForCache(ImmediateCanvas canvas, RenderCache cache)
    {
        if (BlendMode != BlendMode.SrcOver)
            throw new InvalidOperationException("SrcOver以外のブレンドモードはキャッシュ用に描画できません");

        Render(canvas);
    }

    void ISupportRenderCache.RenderWithCache(ImmediateCanvas canvas, RenderCache cache)
    {
        using (Ref<SKSurface> surface = cache.UseCache(out Rect cacheBounds))
        {
            using (canvas.PushBlendMode(BlendMode))
            {
                canvas.DrawSurface(surface.Value, cacheBounds.Position);
            }
        }
    }

    Rect ISupportRenderCache.TransformBoundsForCache(RenderCache cache)
    {
        return Bounds;
    }
}
