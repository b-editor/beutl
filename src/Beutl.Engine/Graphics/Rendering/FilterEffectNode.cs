using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.Rendering.Cache;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class FilterEffectNode(FilterEffect filterEffect) : ContainerNode, ISupportRenderCache
{
    private FilterEffectContext? _prevContext;
    private Rect _rect = Rect.Invalid;

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
        Rect r = _rect;
        if (r.IsInvalid)
        {
            r = FilterEffect.TransformBounds(bounds);
        }
        if (r.IsInvalid)
        {
            r = bounds;
        }

        return r;
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
        EffectTargets effectTargets)
    {
        FilterEffectContext context = GetOrCreateContext();

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(effectTargets, builder, canvas))
        {
            activator.Apply(context, range);


            if (builder.HasFilter())
            {
                using (var paint = new SKPaint())
                {
                    paint.BlendMode = (SKBlendMode)canvas.BlendMode;
                    paint.ImageFilter = builder.GetFilter();

                    foreach (var t in activator.CurrentTargets)
                    {
                        using (canvas.PushBlendMode(BlendMode.SrcOver))
                        using (canvas.PushTransform(Matrix.CreateTranslation(t.Bounds.X - t.OriginalBounds.X, t.Bounds.Y - t.OriginalBounds.Y)))
                        using (canvas.PushPaint(paint))
                        {
                            t.Draw(canvas);
                        }
                    }
                }
            }
            else
            {
                foreach (var t in activator.CurrentTargets)
                {
                    if (t.Surface != null)
                    {
                        canvas.DrawSurface(t.Surface.Value, t.Bounds.Position);
                    }
                    else if (t.Node == this
                        || t != EffectTarget.Empty)
                    {
                        base.Render(canvas);
                    }
                }
            }

            _rect = activator.CurrentTargets.Aggregate<EffectTarget, Rect>(default, (x, y) => x.Union(y.Bounds));
        }
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (EffectTargets targets = [new EffectTarget(this)])
        {
            RenderCore(canvas, Range.All, targets);
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
        using (EffectTargets targets = [new EffectTarget(this)])
        {
            RenderCore(canvas, 0..minNumber, targets);
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
                using (EffectTargets targets = [new EffectTarget(surface, cacheBounds)])
                {
                    RenderCore(canvas, minNumber.., targets);
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
