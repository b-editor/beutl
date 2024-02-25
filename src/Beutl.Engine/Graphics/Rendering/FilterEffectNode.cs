using System.Collections.Immutable;
using System.Diagnostics;

using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Rendering.Cache;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class FilterEffectNode : ContainerNode, ISupportRenderCache
{
    private FilterEffectNodeComparer _comparer;
    private FilterEffectContext? _prevContext;

    private int? _prevVersion;
    private Rect _rect = Rect.Invalid;

    public FilterEffectNode(FilterEffect filterEffect)
    {
        FilterEffect = filterEffect;
        _comparer = new(this);
    }

    public FilterEffect FilterEffect { get; }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _prevContext?.Dispose();
        _prevContext = null;
        _prevVersion = null;
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
           || _prevVersion != FilterEffect.Version)
        {
            context = new FilterEffectContext(Children.Count == 1 ? OriginalBounds : Rect.Invalid);
            context.Apply(FilterEffect);
            _prevContext?.Dispose();
            _prevContext = context;
            _prevVersion = FilterEffect.Version;
        }

        return context;
    }

    private void RenderCore(
        ImmediateCanvas canvas,
        int offset, int? count,
        EffectTargets effectTargets)
    {
        FilterEffectContext context = GetOrCreateContext();

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(effectTargets, builder, canvas))
        {
            activator.Apply(context, offset, count);

            if (builder.HasFilter())
            {
                using (var paint = new SKPaint())
                {
                    paint.BlendMode = (SKBlendMode)canvas.BlendMode;
                    paint.ImageFilter = builder.GetFilter();

                    foreach (EffectTarget t in activator.CurrentTargets)
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
                        || !t.IsEmpty)
                    {
                        base.Render(canvas);
                    }
                }
            }

            _rect = activator.CurrentTargets.CalculateBounds();

            _comparer.OnRender(activator, offset, count);
        }
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (EffectTargets targets = [.. Children.Select(v => new EffectTarget(v))])
        {
            RenderCore(canvas, 0, null, targets);
        }
    }

    void ISupportRenderCache.Accepts(RenderCache cache)
    {
        _comparer.Accepts(cache);
    }

    void ISupportRenderCache.CreateCache(IImmediateCanvasFactory factory, RenderCache cache, RenderCacheContext context)
    {
        int minNumber = cache.GetMinNumber();

        FilterEffectContext fecontext = GetOrCreateContext();

        using (EffectTargets targets = [.. Children.Select(v => new EffectTarget(v))])
        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(targets, builder, factory))
        {
            activator.Apply(fecontext, 0, minNumber);
            activator.Flush(false);

            if (targets.Any(t => !context.CacheOptions.Rules.Match(PixelRect.FromRect(t.Bounds).Size)))
                return;

            // nodeの子要素のキャッシュをすべて削除
            context.ClearCache(this, cache);

            cache.StoreCache([.. activator.CurrentTargets
                .Select(i => (i.Surface, i.Bounds))
                .Where(i => i.Item1 != null)]);
        }

        Debug.WriteLine($"[RenderCache:Created] '{this}[0..{minNumber}]'");
    }

    void ISupportRenderCache.RenderWithCache(ImmediateCanvas canvas, RenderCache cache)
    {
        int minNumber = cache.GetMinNumber();
        FilterEffectContext context = GetOrCreateContext();

        (Ref<SKSurface> Surface, Rect Bounds)[] cacheItems = cache.UseCache();
        try
        {
            using (var targets = new EffectTargets())
            {
                targets.AddRange(cacheItems.Select(i =>
                {
                    SKSurface srcSurface = i.Surface.Value;
                    SKRectI rect = srcSurface.Canvas.DeviceClipBounds;
                    SKSurface newSurface = canvas.CreateRenderTarget(rect.Width, rect.Height)!;
                    newSurface.Canvas.DrawSurface(srcSurface, default);

                    using var surfaceRef = Ref<SKSurface>.Create(newSurface);
                    return new EffectTarget(surfaceRef, i.Bounds)
                    {
                        OriginalBounds = i.Bounds.WithX(0).WithY(0)
                    };
                }));

                RenderCore(canvas, minNumber, null, targets);
            }
        }
        finally
        {
            foreach ((Ref<SKSurface> s, _) in cacheItems)
            {
                s.Dispose();
            }
        }
    }
}
