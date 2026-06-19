using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(
    EffectTargets targets, SKImageFilterBuilder builder, float outputScale = 1f, float workingScale = 1f,
    float maxWorkingScale = float.PositiveInfinity) : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectActivator");

    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTargets CurrentTargets { get; } = targets;

    /// <summary>The render request's output scale <c>s_out</c>. Sanitized to positive-finite.</summary>
    public float OutputScale { get; } = SanitizePositiveFinite(outputScale, nameof(outputScale));

    /// <summary>
    /// Working density <c>w</c> for buffer allocation. Reduced in place by <see cref="Flush"/>
    /// when the dimension clamp fires. Sanitized to positive-finite.
    /// </summary>
    public float WorkingScale { get; private set; } = SanitizePositiveFinite(workingScale, nameof(workingScale));

    /// <summary>Working-scale ceiling forwarded into nested canvases. Sanitized: NaN or non-positive becomes +Inf (no ceiling).</summary>
    public float MaxWorkingScale { get; } = SanitizeCeiling(maxWorkingScale, nameof(maxWorkingScale));

    // Reuses the canonical degenerate-ceiling rule and adds a warning when it actually fires.
    private static float SanitizeCeiling(float value, string name)
    {
        float sanitized = RenderNodeContext.SanitizeMaxWorkingScale(value);
        return sanitized != value ? LogAndFallback(value, name, sanitized) : sanitized;
    }

    private static float SanitizePositiveFinite(float value, string name)
    {
        if (float.IsFinite(value) && value > 0f)
            return value;
        s_logger.LogWarning("FilterEffectActivator: {Param} ({Value}) is not positive-finite; falling back to 1.0.",
            name, value);
        return 1f;
    }

    private static float LogAndFallback(float value, string name, float fallback)
    {
        s_logger.LogWarning("FilterEffectActivator: {Param} ({Value}) is not positive; falling back to {Fallback}.",
            name, value, fallback);
        return fallback;
    }

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

            // Re-clamp working scale: Skia filters may have inflated OriginalBounds past the node-level clamp.
            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                float fit = RenderNodeContext.ClampWorkingScaleToBufferBudget(
                    CurrentTargets[i].OriginalBounds, WorkingScale);
                if (fit < WorkingScale)
                {
                    s_logger.LogWarning(
                        "Working scale clamped {From} -> {To} to keep an effect buffer within the GPU axis limit (bounds {Bounds}).",
                        WorkingScale, fit, CurrentTargets[i].OriginalBounds);
                    WorkingScale = fit;
                }
            }

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                float w = WorkingScale;
                int bw = w == 1f ? (int)target.OriginalBounds.Width : (int)MathF.Ceiling(target.OriginalBounds.Width * w);
                int bh = w == 1f ? (int)target.OriginalBounds.Height : (int)MathF.Ceiling(target.OriginalBounds.Height * w);
                using RenderTarget? surface = RenderTarget.Create(bw, bh);

                if (surface != null)
                {
                    using (var canvas = new ImmediateCanvas(surface, w, MaxWorkingScale,
                               logicalSize: target.OriginalBounds.Size))
                    {
                        canvas.Clear();
                        using (canvas.PushTransform(
                                   Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)))
                        using (paint != null ? canvas.PushPaint(paint) : default)
                        {
                            target.Draw(canvas);
                        }
                    }

                    var newTarget = new EffectTarget(surface, target.Bounds, EffectiveScale.At(w))
                    {
                        OriginalBounds = target.OriginalBounds
                    };
                    CurrentTargets[i] = newTarget;
                    target.Dispose();
                }
                else
                {
                    Rect originalBounds = target.OriginalBounds;
                    // The layer would silently vanish from the output otherwise — make the failure visible.
                    s_logger.LogWarning(
                        "Effect flush buffer allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); preview drops this target, delivery render fails fast.",
                        bw, bh, w, originalBounds);
                    target?.Dispose();

                    ThrowIfDeliveryAllocationFailure(
                        $"Effect flush buffer allocation failed ({bw}x{bh} px, w {w}, bounds {originalBounds}).");

                    CurrentTargets.RemoveAt(i);
                    i--;
                }

            }

            Builder.Clear();
        }
    }

    private void ThrowIfDeliveryAllocationFailure(string message)
    {
        if (float.IsPositiveInfinity(MaxWorkingScale))
        {
            throw new InvalidOperationException(message);
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

                        var customContext = new CustomFilterEffectContext(
                            CurrentTargets, OutputScale, WorkingScale, MaxWorkingScale);
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
        using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds(), OutputScale, WorkingScale);

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
        using var activator = new FilterEffectActivator(cloned, builder, OutputScale, WorkingScale, MaxWorkingScale);

        activator.Apply(context);
        activator.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in activator.CurrentTargets)
        {
            if (t.RenderTarget == null) continue;

            SKSurface innerSurface = t.RenderTarget.Value;
            using SKImage skImage = innerSurface.Snapshot();

            // Dest size from buffer footprint (pixels / density), not from Bounds — Bounds may be
            // inflated by downstream effects.
            SKImageFilter image;
            if (t.Scale.IsUnbounded || t.Scale.Value == 1f)
            {
                image = SKImageFilter.CreateImage(skImage);
            }
            else
            {
                float density = t.Scale.Value;
                var dst = new SKRect(
                    (float)t.Bounds.X,
                    (float)t.Bounds.Y,
                    (float)t.Bounds.X + skImage.Width / density,
                    (float)t.Bounds.Y + skImage.Height / density);
                image = SKImageFilter.CreateImage(
                    skImage,
                    new SKRect(0, 0, skImage.Width, skImage.Height),
                    dst,
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            filter = filter == null ? image : SKImageFilter.CreateCompose(filter, image);
        }

        return filter;
    }
}
