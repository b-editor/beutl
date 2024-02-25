using System.Diagnostics;

using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(EffectTargets targets, SKImageFilterBuilder builder, IImmediateCanvasFactory factory) : IDisposable
{
    private readonly IImmediateCanvasFactory _factory = factory;

    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTargets CurrentTargets { get; private set; } = targets;

    public void Dispose()
    {
    }

    public void Flush(bool force = true)
    {
        if (force
            || Builder.HasFilter()
            || (CurrentTargets.Count == 1 && CurrentTargets[0].Node != null))
        {
            using var paint = new SKPaint
            {
                ImageFilter = Builder.GetFilter(),
            };

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                SKSurface? surface = _factory.CreateRenderTarget((int)target.OriginalBounds.Width, (int)target.OriginalBounds.Height);

                if (surface != null)
                {
                    using ImmediateCanvas canvas = _factory.CreateCanvas(surface, true);

                    using (canvas.PushTransform(Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)))
                    using (canvas.PushPaint(paint))
                    {
                        target.Draw(canvas);
                    }

                    using var surfaceRef = Ref<SKSurface>.Create(surface);
                    var newTarget = new EffectTarget(surfaceRef, target.Bounds)
                    {
                        OriginalBounds = target.OriginalBounds
                    };
                    newTarget._history.AddRange(target._history.Select(v => v.Inherit()));
                    CurrentTargets[i] = newTarget;
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

    // 最小単位である'IFEItem'の数がわからないので 'count'は'nullable'
    public void Apply(FilterEffectContext context, int offset, int? count)
    {
        if (CurrentTargets.Count == 0) return;

        int takeCount;
        if (count.HasValue)
        {
            takeCount = Math.Min(count.Value, context._items.Count);
        }
        else
        {
            takeCount = Math.Max(context._items.Count - offset, 0);
        }
        foreach (FEItemWrapper item in context._items.Skip(offset).Take(takeCount))
        {
            if (item.Item is IFEItem_Skia skia)
            {
                skia.Accepts(this, Builder);
                foreach (EffectTarget t in CurrentTargets)
                {
                    t._history.Add(item);
                    t.Bounds = item.Item.TransformBounds(t.Bounds);
                    t.OriginalBounds = item.Item.TransformBounds(t.OriginalBounds);
                }
            }
            else if (item.Item is IFEItem_Custom custom)
            {
                Flush(true);
                if (CurrentTargets.Count == 0) return;

                var customContext = new CustomFilterEffectContext(
                    _factory,
                    CurrentTargets,
                    [.. CurrentTargets[0]._history.Select(v => v.Inherit())]);
                custom.Accepts(customContext);

                foreach (EffectTarget t in CurrentTargets)
                {
                    t._history.Add(item);
                    //t.Bounds = item.TransformBounds(t.Bounds);
                    t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                }
            }
        }

        // 適用したIFEItemの数だけずらす
        if (count.HasValue)
        {
            count = count.Value - takeCount;
        }

        offset = Math.Max(offset - context._items.Count, 0);

        if (context._renderTimeItems.Count > 0)
        {
            Flush(false);
            if (CurrentTargets.Count == 0) return;

            FEItemWrapper? deferral = null;
            object[]? deferralItems = null;
            bool deferred = false;

            // 後の `if(deferred)` で使う
            int offset_ = 0;
            int? count_ = null;

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget t = CurrentTargets[i];

                using (var ctx = new FilterEffectContext(t.Bounds))
                {
                    foreach (object item in context._renderTimeItems)
                    {
                        if (item is FilterEffect fe)
                        {
                            ctx.Apply(fe);
                        }
                        else if (item is FEItemWrapper feitem)
                        {
                            ctx._items.Add(feitem);
                        }
                    }

                    if (i == 0)
                    {
                        deferred = ctx.Bounds.IsInvalid;
                        if (deferred)
                        {
                            deferral = ctx._items[^1];
                            deferralItems = [.. ctx._renderTimeItems];
                        }
                    }

                    Debug.Assert(deferred == ctx.Bounds.IsInvalid);

                    if (ctx.Bounds.IsInvalid)
                    {
                        // boundsが無効な場合
                        // 無効になる手前まで、エフェクトを適用する
                        Rect b = t.Bounds;
                        for (int ii = 0; ii < ctx._items.Count - 1; ii++)
                        {
                            b = ctx._items[ii].Item.TransformBounds(b);
                        }

                        using (FilterEffectContext safeContext = ctx.Clone())
                        using (var builder = new SKImageFilterBuilder())
                        using (var activator = new FilterEffectActivator([t], builder, _factory))
                        {
                            safeContext.Bounds = b;
                            safeContext._items.RemoveAt(safeContext._items.Count - 1);
                            safeContext._renderTimeItems.Clear();

                            activator.Apply(safeContext, offset, count);
                            activator.Flush(false);

                            CurrentTargets.RemoveAt(i);
                            CurrentTargets.InsertRange(i, activator.CurrentTargets);
                            i += activator.CurrentTargets.Count - 1;

                            if (i == 0)
                            {
                                int takeCount_;
                                if (count.HasValue)
                                {
                                    takeCount_ = Math.Min(count.Value, safeContext._items.Count);
                                    count_ = count.Value - takeCount_;
                                }
                                else
                                {
                                    takeCount_ = Math.Max(safeContext._items.Count - offset, 0);
                                }

                                offset_ = Math.Max(offset - safeContext._items.Count, 0);
                            }
                        }
                    }
                    else
                    {
                        using (var builder = new SKImageFilterBuilder())
                        using (var activator = new FilterEffectActivator([t], builder, _factory))
                        {
                            activator.Apply(ctx, offset, count);
                            activator.Flush(false);

                            CurrentTargets.RemoveAt(i);
                            CurrentTargets.InsertRange(i, activator.CurrentTargets);
                            i += activator.CurrentTargets.Count - 1;

                            if (i == 0)
                            {
                                int takeCount_;
                                if (count.HasValue)
                                {
                                    takeCount_ = Math.Min(count.Value, ctx._items.Count);
                                    count_ = count.Value - takeCount_;
                                }
                                else
                                {
                                    takeCount_ = Math.Max(ctx._items.Count - offset, 0);
                                }

                                offset_ = Math.Max(offset - ctx._items.Count, 0);
                            }
                        }
                    }
                }
            }

            offset = offset_;
            count = count_;

            if (deferred && (!count.HasValue || count > 0) && CurrentTargets.Count > 0)
            {
                if (offset == 0)
                {
                    Flush(false);

                    var customContext = new CustomFilterEffectContext(
                        _factory,
                        CurrentTargets,
                        [.. CurrentTargets[0]._history.Select(v => v.Inherit())]);
                    ((IFEItem_Custom)deferral!.Item).Accepts(customContext);
                    foreach (EffectTarget t in CurrentTargets)
                    {
                        // deferral
                        t._history.Add(deferral);
                        //t.Bounds = item.TransformBounds(t.Bounds);
                        t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                    }
                }

                if (count.HasValue)
                {
                    count = count.Value - 1;
                }
                offset = Math.Max(offset - 1, 0);

                using (var builder = new SKImageFilterBuilder())
                using (var activator = new FilterEffectActivator(CurrentTargets, builder, _factory))
                using (var ctx = new FilterEffectContext(Rect.Invalid))
                {
                    foreach (object item in deferralItems!)
                    {
                        if (item is FilterEffect fe)
                        {
                            ctx.Apply(fe);
                        }
                        else if (item is FEItemWrapper feitem)
                        {
                            ctx._items.Add(feitem);
                        }
                    }

                    activator.Apply(ctx, offset, count);
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
        Apply(context, 0, null);
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        SKImageFilter? filter;
        Flush(false);
        using (EffectTargets cloned = CurrentTargets.Clone())
        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(cloned, builder, _factory))
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
