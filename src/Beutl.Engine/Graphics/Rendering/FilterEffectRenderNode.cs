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
                var imageFilter = builder.GetFilter();
                return activator.CurrentTargets.Select(t =>
                {
                    var paint = new SKPaint();
                    paint.ImageFilter = imageFilter;
                    return RenderNodeOperation.CreateLambda(
                        bounds: t.Bounds,
                        render: canvas =>
                        {
                            using (canvas.PushBlendMode(BlendMode.SrcOver))
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
                        }
                        // CorrectionScale: this Lambda materialises the filter via PushPaint / SaveLayer,
                        // which Skia allocates at the compositor canvas's output scale rather than at the
                        // upstream raster scale. The Lambda's content is therefore already full-resolution
                        // by the time it reaches the compositor — emit Identity (the factory default) so
                        // the compositor's blit doesn't add a second upscale that would push the rendered
                        // content off-canvas via the Scale-around-bounds-pivot. The no-filter branch below
                        // returns rasters that ARE at upstream scale (via primitive-driven Flush or
                        // CustomEffect-allocated RTs), so it keeps `unifiedScale`.
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
