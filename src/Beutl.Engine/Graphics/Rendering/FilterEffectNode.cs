using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.Rendering.Cache;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class FilterEffectNode(FilterEffect filterEffect) : ContainerNode, ISupportRenderCache
{
    private FilterEffectContext? _prevContext;

    public FilterEffect FilterEffect { get; } = filterEffect;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _prevContext?.Dispose();
        _prevContext = null;
    }

    public override bool HitTest(Point point)
    {
        if (_prevContext?.CountItems() > 0)
        {
            return Bounds.Contains(point);
        }
        else
        {
            return base.HitTest(point);
        }
    }

    public bool Equals(FilterEffect filterEffect)
    {
        return FilterEffect == filterEffect;
    }

    protected override Rect TransformBounds(Rect bounds)
    {
        return FilterEffect.TransformBounds(bounds);
    }

    private FilterEffectContext GetOrCreateContext()
    {
        FilterEffectContext? context = _prevContext;
        if (context == null
           || _prevContext?.FirstVersion() != FilterEffect.Version)
        {
            context = new FilterEffectContext(OriginalBounds);
            context.Apply(FilterEffect);
            _prevContext?.Dispose();
            _prevContext = context;
        }

        return context;
    }

    private void RenderCore(
        ImmediateCanvas canvas,
        Range range,
        EffectTarget effectTarget,
        Rect originalBounds)
    {
        FilterEffectContext context = GetOrCreateContext();

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(originalBounds, effectTarget, builder, canvas))
        {
            activator.Apply(context, range);

#if true
            if (builder.HasFilter())
            {
                using (var paint = new SKPaint())
                {
                    paint.BlendMode = (SKBlendMode)canvas.BlendMode;
                    paint.ImageFilter = builder.GetFilter();

                    using (canvas.PushBlendMode(BlendMode.SrcOver))
                    using (canvas.PushTransform(Matrix.CreateTranslation(activator.Bounds.X - activator.OriginalBounds.X, activator.Bounds.Y - activator.OriginalBounds.Y)))
                    using (canvas.PushPaint(paint))
                    {
                        activator.CurrentTarget.Draw(canvas);
                    }
                }
            }
            else
#else
            // 上のコードは、フレームバッファごと回転してしまうことがあった
            // (SaveLayerでlilmitを指定しても)
            activator.Flush(false);
#endif
            if (activator.CurrentTarget.Surface != null)
            {
                canvas.DrawSurface(activator.CurrentTarget.Surface.Value, activator.Bounds.Position);
            }
            else if (activator.CurrentTarget.Node == this
                || activator.CurrentTarget != EffectTarget.Empty)
            {
                base.Render(canvas);
            }
        }
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (var target = new EffectTarget(this))
        {
            RenderCore(canvas, Range.All, target, OriginalBounds);
        }
    }

    void ISupportRenderCache.Accepts(RenderCache cache)
    {
        if (_prevContext != null
            && _prevContext.FirstVersion() == FilterEffect.Version)
        {
            int count = _prevContext.CountItems();
            cache.ReportSameNumber(count, count);
        }
        else
        {
            var context = new FilterEffectContext(OriginalBounds);
            context.Apply(FilterEffect);

            // 新しく作成したコンテキストと前回のコンテキストがどこまで同じかをカウント
            int count = context.CountEquals(_prevContext);
            cache.ReportSameNumber(count, context.CountItems());

            _prevContext?.Dispose();
            _prevContext = context;
        }
    }

    void ISupportRenderCache.RenderForCache(ImmediateCanvas canvas, RenderCache cache)
    {
        int minNumber = cache.GetMinNumber();
        using (var target = new EffectTarget(this))
        {
            RenderCore(canvas, 0..minNumber, target, OriginalBounds);
        }
    }

    void ISupportRenderCache.RenderWithCache(ImmediateCanvas canvas, RenderCache cache)
    {
        int minNumber = cache.GetMinNumber();
        FilterEffectContext context = GetOrCreateContext();

        using (Ref<SKSurface> surface = cache.UseCache(out Rect cacheBounds))
        {
            if (context.CountItems() == minNumber)
            {
                canvas.DrawSurface(surface.Value, cacheBounds.Position);
            }
            else
            {
                using (var target = new EffectTarget(surface, cacheBounds.Size))
                {
                    RenderCore(canvas, minNumber.., target, cacheBounds);
                }
            }
        }
    }

    Rect ISupportRenderCache.TransformBoundsForCache(RenderCache cache)
    {
        int minNumber = cache.GetMinNumber();
        FilterEffectContext context = GetOrCreateContext();

        return context.TransformBounds(0..minNumber);
    }
}
