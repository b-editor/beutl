using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(EffectTargets targets, SKImageFilterBuilder builder, float workingScale = 1f) : IDisposable
{
    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTargets CurrentTargets { get; } = targets;

    /// <summary>
    /// The working density <c>w</c> at which buffer-allocating boundaries rasterize (feature 003,
    /// FR-009). <c>1.0</c> keeps the exact pre-feature <c>(int)</c>-truncation path (byte-identical).
    /// </summary>
    public float WorkingScale { get; } = workingScale;

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

            float w = WorkingScale;
            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                // feature 003: at w != 1 size the flattened buffer ceil(OriginalBounds × w) device px and
                // prescale by w, so the chain rasterizes at working density; w == 1 keeps the exact
                // (int)-truncation + translation-only path (byte-identical).
                int bw = w == 1f ? (int)target.OriginalBounds.Width : (int)MathF.Ceiling(target.OriginalBounds.Width * w);
                int bh = w == 1f ? (int)target.OriginalBounds.Height : (int)MathF.Ceiling(target.OriginalBounds.Height * w);
                using RenderTarget? surface = RenderTarget.Create(bw, bh);

                if (surface != null)
                {
                    using (var canvas = new ImmediateCanvas(surface))
                    {
                        canvas.Clear();
                        Matrix transform = w == 1f
                            ? Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)
                            : Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y) * Matrix.CreateScale(w, w);
                        using (canvas.PushTransform(transform))
                        using (paint != null ? canvas.PushPaint(paint) : default)
                        {
                            target.Draw(canvas);
                        }
                    }

                    var newTarget = new EffectTarget(surface, target.Bounds, w == 1f ? target.Scale : EffectiveScale.At(w))
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

                        var customContext = new CustomFilterEffectContext(CurrentTargets, WorkingScale);
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
        using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds(), workingScale: WorkingScale);

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
        using var activator = new FilterEffectActivator(cloned, builder, WorkingScale);

        activator.Apply(context);
        activator.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in activator.CurrentTargets)
        {
            if (t.RenderTarget == null) continue;

            SKSurface innerSurface = t.RenderTarget.Value;
            using SKImage skImage = innerSurface.Snapshot();

            // feature 003: a buffer captured At(w) is ceil(bounds × w) device px; map it back into its
            // logical footprint so the composed filter stays in logical space. Unbounded / unit-scale
            // keeps the bare CreateImage (byte-identical).
            SKImageFilter image = t.Scale.IsUnbounded || t.Scale.Value == 1f
                ? SKImageFilter.CreateImage(skImage)
                : SKImageFilter.CreateImage(
                    skImage,
                    new SKRect(0, 0, skImage.Width, skImage.Height),
                    t.Bounds.ToSKRect(),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));

            filter = filter == null ? image : SKImageFilter.CreateCompose(filter, image);
        }

        return filter;
    }
}
