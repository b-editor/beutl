using Beutl.Graphics.Rendering;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(EffectTargets targets, SKImageFilterBuilder builder, IImmediateCanvasFactory factory) : IDisposable
{
    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTargets CurrentTargets { get; } = targets;

    public void Dispose()
    {
    }

    public void Flush(bool force = true)
    {
        if (force
            || Builder.HasFilter()
            || CurrentTargets is [{ NodeOperation: not null }])
        {
            using var paint = new SKPaint();
            paint.ImageFilter = Builder.GetFilter();

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                RenderTarget? surface = RenderTarget.Create((int)target.OriginalBounds.Width, (int)target.OriginalBounds.Height);

                if (surface != null)
                {
                    using (var canvas = new ImmediateCanvas(surface))
                    using (canvas.PushTransform(Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)))
                    using (canvas.PushPaint(paint))
                    {
                        target.Draw(canvas);
                    }

                    var newTarget = new EffectTarget(surface, target.Bounds)
                    {
                        OriginalBounds = target.OriginalBounds
                    };
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
    public void Apply(FilterEffectContext context)
    {
        if (CurrentTargets.Count == 0) return;

        foreach (FEItemWrapper item in context._items)
        {
            switch (item.Item)
            {
                case IFEItem_Skia skia:
                    {
                        skia.Accepts(this, Builder);
                        foreach (EffectTarget t in CurrentTargets)
                        {
                            t.Bounds = item.Item.TransformBounds(t.Bounds);
                            t.OriginalBounds = item.Item.TransformBounds(t.OriginalBounds);
                        }

                        break;
                    }
                case IFEItem_Custom custom:
                    {
                        Flush();
                        if (CurrentTargets.Count == 0) return;

                        var customContext = new CustomFilterEffectContext(CurrentTargets);
                        custom.Accepts(customContext);

                        foreach (EffectTarget t in CurrentTargets)
                        {
                            //t.Bounds = item.TransformBounds(t.Bounds);
                            t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                        }

                        break;
                    }
            }
        }

        if (context._renderTimeItems.Count <= 0) return;

        Flush(false);
        if (CurrentTargets.Count == 0) return;
        using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds());

        foreach (object item in context._renderTimeItems)
        {
            switch (item)
            {
                case FilterEffect fe:
                    ctx.Apply(fe);
                    break;
                case FEItemWrapper feitem:
                    ctx._items.Add(feitem);
                    break;
            }
        }

        Apply(ctx);
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        Flush(false);

        using EffectTargets cloned = CurrentTargets.Clone();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(cloned, builder, factory);

        activator.Apply(context);
        activator.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in activator.CurrentTargets)
        {
            if (t.Surface == null) continue;

            SKSurface innerSurface = t.Surface.Value;
            using SKImage skImage = innerSurface.Snapshot();

            if (filter == null)
            {
                filter = SKImageFilter.CreateImage(skImage);
            }
            else
            {
                filter = SKImageFilter.CreateCompose(filter, SKImageFilter.CreateImage(skImage));
            }
        }

        return filter;
    }
}
