using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(EffectTargets targets, SKImageFilterBuilder builder) : IDisposable
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
            using var paint = Builder.HasFilter() ? new SKPaint() : null;
            paint?.ImageFilter = Builder.GetFilter();

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                // Allocate the materialised raster at the target's CorrectionScale so the chain stays
                // consistent with upstream proxy. OriginalBounds is in authoring; the RT is sized down.
                RenderScale scale = target.CorrectionScale;
                int rasterW, rasterH;
                if (scale.IsIdentity)
                {
                    rasterW = (int)target.OriginalBounds.Width;
                    rasterH = (int)target.OriginalBounds.Height;
                }
                else
                {
                    rasterW = Math.Max(1, (int)MathF.Ceiling(target.OriginalBounds.Width / scale.ScaleX));
                    rasterH = Math.Max(1, (int)MathF.Ceiling(target.OriginalBounds.Height / scale.ScaleY));
                }
                using RenderTarget? surface = RenderTarget.Create(rasterW, rasterH);

                if (surface != null)
                {
                    using (var canvas = new ImmediateCanvas(surface))
                    {
                        canvas.Clear();
                        // The materialised RT is at upstream scale (rasterW/H). The translate is in
                        // authoring units but lands correctly because the upstream NodeOperation's
                        // raster is sized in physical pixels equal to the new RT, so authoring units
                        // and physical pixel units of the new RT compose 1:1 along the translation.
                        using (canvas.PushTransform(Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)))
                        using (paint != null ? canvas.PushPaint(paint) : default)
                        {
                            target.Draw(canvas);
                        }
                    }

                    var newTarget = new EffectTarget(surface, target.Bounds, scale)
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

        foreach (IFEItem item in context._items)
        {
            switch (item)
            {
                case IFEItem_Skia skia:
                    {
                        skia.Accepts(this, Builder);
                        foreach (EffectTarget t in CurrentTargets)
                        {
                            t.Bounds = item.TransformBounds(t.Bounds);
                            t.OriginalBounds = item.TransformBounds(t.OriginalBounds);
                        }

                        break;
                    }
                case IFEItem_Custom custom:
                    {
                        Flush();
                        if (CurrentTargets.Count == 0) return;

                        var customContext = new CustomFilterEffectContext(CurrentTargets, context.CorrectionScale);
                        custom.Accepts(customContext);

                        foreach (EffectTarget t in CurrentTargets)
                        {
                            t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                        }

                        break;
                    }
            }
        }

        if (context._renderTimeItems.Count <= 0) return;

        Flush(false);
        if (CurrentTargets.Count == 0) return;
        using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds())
        {
            CorrectionScale = context.CorrectionScale,
        };

        foreach (IFEItem item in context._renderTimeItems)
        {
            ctx._items.Add(item);
        }

        Apply(ctx);
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        Flush(false);

        using EffectTargets cloned = CurrentTargets.Clone();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(cloned, builder);

        activator.Apply(context);
        activator.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in activator.CurrentTargets)
        {
            if (t.RenderTarget == null) continue;

            SKSurface innerSurface = t.RenderTarget.Value;
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
