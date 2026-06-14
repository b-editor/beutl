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

    /// <summary>
    /// The render request's output scale <c>s_out</c> (feature 003, FR-015), forwarded into the nested
    /// <see cref="FilterEffectContext"/> / <see cref="CustomFilterEffectContext"/> it builds so they expose
    /// the real output scale rather than defaulting to <c>1.0</c>.
    /// </summary>
    public float OutputScale { get; } = outputScale;

    /// <summary>
    /// The working density <c>w</c> at which buffer-allocating boundaries rasterize (feature 003,
    /// FR-009). <c>1.0</c> keeps the exact pre-feature <c>(int)</c>-truncation path (byte-identical).
    /// When the FR-037(b) dimension clamp fires in <see cref="Flush"/> this is reduced in place
    /// (monotonically), so the <see cref="CustomFilterEffectContext"/> built afterwards sees the SAME
    /// density the flushed buffers were actually rasterized at — a custom effect's device math
    /// (<c>× WorkingScale</c>) must never disagree with its input buffers' real density.
    /// </summary>
    public float WorkingScale { get; private set; } = workingScale;

    /// <summary>
    /// The render request's working-scale ceiling (feature 003, FR-037), forwarded into the nested
    /// canvases this activator opens so pulls started from them (drawable brushes, nested drawables)
    /// stay under the request's ceiling.
    /// </summary>
    public float MaxWorkingScale { get; } = maxWorkingScale;

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

            // feature 003 (FR-037 backstop): re-clamp the working scale against the targets' bounds at the
            // actual allocation site. The node-level clamp (FilterEffectRenderNode) bounds w against the
            // pre-effect input bounds, but a Skia blur/shadow/dilate inflates OriginalBounds by sigma×3
            // BEFORE this flush, so the real buffer ceil(OriginalBounds × w) can still exceed the GPU limit.
            // The clamp is applied UNIFORMLY (min across targets) and written back to WorkingScale so every
            // buffer in this boundary shares one density and downstream device math stays consistent; inert
            // (keeps w) when everything fits, so w == 1 stays 1 (byte-identical).
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
                // at w != 1 size the flattened buffer ceil(OriginalBounds × w) device px and prescale by w, so
                // the chain rasterizes at working density; w == 1 keeps the exact (int)-truncation +
                // translation-only path (byte-identical).
                int bw = w == 1f ? (int)target.OriginalBounds.Width : (int)MathF.Ceiling(target.OriginalBounds.Width * w);
                int bh = w == 1f ? (int)target.OriginalBounds.Height : (int)MathF.Ceiling(target.OriginalBounds.Height * w);
                using RenderTarget? surface = RenderTarget.Create(bw, bh);

                if (surface != null)
                {
                    // feature 003: this nested buffer renders at working density w — the canvas bakes the base
                    // CTM CreateScale(w) (identity at w == 1) and tags its surface density w, so a SourceBackdrop
                    // captured HERE records its true device density (the backdrop replay un-scales by it). The
                    // flatten only needs the logical translation; w == 1 keeps the byte-identical path.
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

                    // feature 003: the flattened buffer is a concrete bitmap at the working density it was just
                    // rasterized at, so tag it At(w) — including w == 1. (The old w == 1 path inherited the
                    // child's Scale, which over-reported the density of a buffer actually flattened at w == 1.)
                    // At(1) still takes the point-blit branch, so it stays cheap.
                    var newTarget = new EffectTarget(surface, target.Bounds, EffectiveScale.At(w))
                    {
                        OriginalBounds = target.OriginalBounds
                    };
                    CurrentTargets[i] = newTarget;
                    target.Dispose();
                }
                else
                {
                    // The layer would silently vanish from the output otherwise — make the failure visible.
                    s_logger.LogWarning(
                        "Effect flush buffer allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); dropping this target from the output.",
                        bw, bh, w, target.OriginalBounds);
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

                        // WorkingScale here reflects any clamp Flush just applied, so the custom effect's
                        // device math matches its input buffers' actual density.
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

            // feature 003: a buffer captured At(w) is ceil(bounds × w) device px; map it back into its
            // logical footprint so the composed filter stays in logical space. Unbounded / unit-scale
            // keeps the bare CreateImage (byte-identical). Size the destination from the BUFFER footprint
            // (device px ÷ density) anchored at t.Bounds.Position — NOT t.Bounds's size — mirroring
            // EffectTarget.Draw / CreateFromRenderTarget so a downstream filter that inflated Bounds while the
            // buffer still holds the original area cannot stretch the content.
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
