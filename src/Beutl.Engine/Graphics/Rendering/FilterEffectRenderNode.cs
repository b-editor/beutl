using Beutl.Engine;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
    public (FilterEffect.Resource Resource, int Version)? FilterEffect { get; private set; } = filterEffect.Capture();

    public bool Update(FilterEffect.Resource? fe)
    {
        if (!fe.Compare(FilterEffect))
        {
            FilterEffect = fe.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (FilterEffect == null || !FilterEffect.Value.Resource.IsEnabled)
        {
            return context.Input;
        }

        // Pattern Y from contracts/transformer-node-scale-handling.md: a filter that composites
        // multiple upstream rasters into one unified output picks ComponentWiseMax of upstream scales
        // so the lowest-resolution input sets the effective ceiling.
        RenderScale unifiedScale = ComputeUnifiedScale(context.Input);

        using var feContext = new FilterEffectContext(context.CalculateBounds())
        {
            CorrectionScale = unifiedScale,
        };
        FilterEffect.Value.Resource.GetOriginal().ApplyTo(feContext, FilterEffect.Value.Resource);
        var effectTargets = new EffectTargets();
        effectTargets.AddRange(context.Input.Select(i => new EffectTarget(i)));

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(effectTargets, builder))
        {
            activator.Apply(feContext);

            if (builder.HasFilter())
            {
                Rect upstreamBounds = feContext.OriginalBounds;
                var imageFilter = builder.GetFilter();
                return activator.CurrentTargets.Select(t =>
                {
                    var paint = new SKPaint();
                    paint.ImageFilter = imageFilter;
                    // Compositor pivot compensation. The compositor in `RenderNodeProcessor` applies
                    // `Scale(unifiedScale.ScaleX, unifiedScale.ScaleY)` around `op.Bounds.TopLeft` —
                    // but for filter-halo-extended bounds (Blur, DropShadow, etc.) that pivot is the
                    // *extended* top-left, not the upstream raster's anchor. Drawing the upstream raster
                    // at its own origin under that pivot lands it off-canvas. We compute the translate
                    // that converts the compositor's `Scale-around(op.Bounds.TopLeft)` into the
                    // logically-correct `Scale-around(upstreamBounds.TopLeft)`. At Identity scale the
                    // compensation collapses to (0, 0) and this Push is a no-op.
                    // Derivation in Beutl's row-vector matrix convention:
                    // existing compositor matrix M31 = op.Bounds.X * (1 - scaleX). Desired pivot at
                    // `upstreamBounds.X` gives target M31 = upstreamBounds.X * (1 - scaleX).
                    // `Transform.Prepend(translate(tx, ty))` produces `new * existing` whose
                    // `M31 = tx * existing.M11 + existing.M31 = tx * scaleX + existing.M31`. Solving
                    // for tx: `tx = (target.M31 - existing.M31) / scaleX = ((upstreamBounds.X - op.Bounds.X) * (1 - scaleX)) / scaleX`.
                    // At Identity (scaleX = 1) the numerator is zero and the formula collapses to 0.
                    float compensateX = !unifiedScale.IsIdentity
                        ? (upstreamBounds.X - t.Bounds.X) * (1f - unifiedScale.ScaleX) / unifiedScale.ScaleX
                        : 0f;
                    float compensateY = !unifiedScale.IsIdentity
                        ? (upstreamBounds.Y - t.Bounds.Y) * (1f - unifiedScale.ScaleY) / unifiedScale.ScaleY
                        : 0f;

                    return RenderNodeOperation.CreateLambda(
                        bounds: t.Bounds,
                        render: canvas =>
                        {
                            using (canvas.PushBlendMode(BlendMode.SrcOver))
                            using (canvas.PushTransform(Matrix.CreateTranslation(compensateX, compensateY)))
                            using (canvas.PushTransform(Matrix.CreateTranslation(
                                       t.Bounds.X - t.OriginalBounds.X,
                                       t.Bounds.Y - t.OriginalBounds.Y)))
                            using (canvas.PushPaint(paint))
                            {
                                t.Draw(canvas);
                            }
                        },
                        hitTest: t.Bounds.Contains,
                        onDispose: () =>
                        {
                            t.Dispose();
                            paint.Dispose();
                        },
                        correctionScale: unifiedScale
                    );
                }).ToArray();
            }
            else
            {
                return activator.CurrentTargets.Select(i =>
                    i.NodeOperation ??
                    RenderNodeOperation.CreateFromRenderTarget(i.Bounds, i.Bounds.Position, i.RenderTarget!, correctionScale: unifiedScale))
                    .ToArray();
            }
        }
    }

    private static RenderScale ComputeUnifiedScale(RenderNodeOperation[] inputs)
    {
        if (inputs.Length == 0) return RenderScale.Identity;
        float sx = 1f, sy = 1f;
        foreach (var op in inputs)
        {
            var s = op.CorrectionScale;
            if (s.ScaleX > sx) sx = s.ScaleX;
            if (s.ScaleY > sy) sy = s.ScaleY;
        }
        return sx == 1f && sy == 1f ? RenderScale.Identity : new RenderScale(sx, sy);
    }
}
