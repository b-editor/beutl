using System.Diagnostics;

using Beutl.Collections.Pooled;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(EffectTargets targets, SKImageFilterBuilder builder, ImmediateCanvas canvas) : IDisposable
{
    private readonly ImmediateCanvas _canvas = canvas;

    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTargets CurrentTargets { get; private set; } = targets;

    public void Dispose()
    {
    }

    public void Flush(bool force = true)
    {
        if (force || Builder.HasFilter())
        {
            using var paint = new SKPaint
            {
                ImageFilter = Builder.GetFilter(),
            };

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                SKSurface? surface = _canvas.CreateRenderTarget((int)target.OriginalBounds.Width, (int)target.OriginalBounds.Height);

                if (surface != null)
                {
                    using ImmediateCanvas canvas = _canvas.CreateCanvas(surface, true);

                    using (canvas.PushTransform(Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)))
                    using (canvas.PushPaint(paint))
                    {
                        target.Draw(canvas);
                    }

                    using var surfaceRef = Ref<SKSurface>.Create(surface);
                    CurrentTargets[i] = new EffectTarget(surfaceRef, target.Bounds)
                    {
                        OriginalBounds = target.OriginalBounds
                    };
                    target.Dispose();
                }
                else
                {
                    target?.Dispose();

                    CurrentTargets.RemoveAt(i);
                    i--;
                }

            }

            Builder.Clear();
        }
    }

    public void Apply(FilterEffectContext context, Range range)
    {
        (int offset, int count) = range.GetOffsetAndLength(context._items.Count);
        int endAt = offset + count;

        int index = 0;
        foreach (IFEItem item in context._items.Span)
        {
            if (offset <= index && index < endAt)
            {
                if (item is IFEItem_Skia skia)
                {
                    skia.Accepts(this, Builder);
                    foreach (EffectTarget t in CurrentTargets)
                    {
                        t.Bounds = item.TransformBounds(t.Bounds);
                        t.OriginalBounds = item.TransformBounds(t.OriginalBounds);
                    }
                }
                else if (item is IFEItem_Custom custom)
                {
                    Flush(true);
                    var customContext = new FilterEffectCustomOperationContext(_canvas, CurrentTargets);
                    custom.Accepts(customContext);

                    foreach (EffectTarget t in CurrentTargets)
                    {
                        //t.Bounds = item.TransformBounds(t.Bounds);
                        t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                    }
                }
            }

            index++;
        }

        // NOTE: 一旦ノードキャッシュ無しで考えるので、rangeは無視
        if (context._renderTimeItems.Count > 0)
        {
            Flush(false);

            IFEItem_Custom? deferral = null;
            FilterEffect[]? deferralItems = null;
            bool deferred = false;

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget t = CurrentTargets[i];

                using (var ctx = new FilterEffectContext(t.Bounds))
                {
                    foreach (FilterEffect fe in context._renderTimeItems)
                    {
                        ctx.Apply(fe);
                    }

                    if (i == 0)
                    {
                        deferred = ctx.Bounds.IsInvalid;
                    }

                    Debug.Assert(deferred == ctx.Bounds.IsInvalid);

                    if (ctx.Bounds.IsInvalid)
                    {
                        deferral = (IFEItem_Custom)ctx._items[^1];
                        deferralItems = ctx._renderTimeItems.ToArray();

                        // boundsが無効な場合
                        // 無効になる手前まで、エフェクトを適用する
                        Rect b = t.Bounds;
                        for (int ii = 0; ii < ctx._items.Count - 1; ii++)
                        {
                            b = ctx._items[ii].TransformBounds(b);
                        }

                        using (FilterEffectContext safeContext = ctx.Clone())
                        using (var builder = new SKImageFilterBuilder())
                        using (var activator = new FilterEffectActivator([t], builder, _canvas))
                        {
                            safeContext.Bounds = b;
                            safeContext._items.RemoveAt(safeContext._items.Count - 1);
                            safeContext._renderTimeItems.Clear();

                            activator.Apply(safeContext, Range.All);
                            activator.Flush(false);

                            CurrentTargets.RemoveAt(i);
                            CurrentTargets.InsertRange(i, activator.CurrentTargets);
                            i += activator.CurrentTargets.Count - 1;
                        }
                    }
                    else
                    {
                        using (var builder = new SKImageFilterBuilder())
                        using (var activator = new FilterEffectActivator([t], builder, _canvas))
                        {
                            activator.Apply(ctx, Range.All);
                            activator.Flush(false);

                            CurrentTargets.RemoveAt(i);
                            CurrentTargets.InsertRange(i, activator.CurrentTargets);
                            i += activator.CurrentTargets.Count - 1;
                        }
                    }
                }
            }

            if (deferred)
            {
                Flush(false);
                var customContext = new FilterEffectCustomOperationContext(_canvas, CurrentTargets);
                deferral!.Accepts(customContext);
                foreach (EffectTarget t in CurrentTargets)
                {
                    //t.Bounds = item.TransformBounds(t.Bounds);
                    t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                }

                using (var builder = new SKImageFilterBuilder())
                using (var activator = new FilterEffectActivator(CurrentTargets, builder, _canvas))
                using (var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds()))
                {
                    foreach (FilterEffect fe in deferralItems!)
                    {
                        ctx.Apply(fe);
                    }

                    activator.Apply(ctx, Range.All);
                    activator.Flush(false);

                    // `activator.CurrentTargets` と `this.CurrentTargets` は同じインスタンス
                    //CurrentTargets.Clear();
                    //CurrentTargets.AddRange(activator.CurrentTargets);
                }
            }
        }
    }

    public void Apply(FilterEffectContext context)
    {
        Apply(context, Range.All);
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        SKImageFilter? filter;
        Flush(false);
        using (EffectTargets cloned = CurrentTargets.Clone())
        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(cloned, builder, _canvas))
        {
            activator.Apply(context);

            activator.Flush(false);

            filter = builder.GetFilter();
            if (filter == null)
            {
                foreach (EffectTarget t in activator.CurrentTargets)
                {
                    if (t.Surface != null)
                    {
                        SKSurface innerSurface = t.Surface.Value;
                        using (SKImage skImage = innerSurface.Snapshot())
                        {
                            if (filter == null)
                            {
                                filter = SKImageFilter.CreateImage(skImage);
                            }
                            else
                            {
                                filter = SKImageFilter.CreateCompose(filter, SKImageFilter.CreateImage(skImage));
                            }
                        }
                    }
                }
            }
        }

        return filter;
    }
}
