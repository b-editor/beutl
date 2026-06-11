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

        // Resolve this effect's working scale w from its inputs' supply densities (feature 003, FR-036).
        // Supply-driven: w == the densest concrete input, capped by the global memory ceiling. Threaded into
        // the context (FR-015) AND into the activator, which sizes render-target / Custom buffers
        // ceil(bounds × w) and resamples them once at the final blit (FilterEffectActivator,
        // CustomFilterEffectContext, EffectTarget.Draw). At output scale 1.0 with vector inputs, w == 1, so
        // every buffer keeps its (int)-truncation point-blit path (byte-identical). An effect that needs a
        // different w (clamp for perf, oversample for SSAA) overrides Process in a FilterEffectRenderNode
        // subclass returned from FilterEffect.Resource.CreateRenderNode().
        var inputScales = new EffectiveScale[context.Input.Length];
        for (int i = 0; i < context.Input.Length; i++)
        {
            inputScales[i] = context.Input[i].EffectiveScale;
        }

        float workingScale = RenderNodeContext.ResolveWorkingScale(
            inputScales, context.OutputScale, context.MaxWorkingScale);

        // FR-037 memory / GPU-texture backstop: the FR-037 ceiling bounds w, but the buffer (and its memory)
        // scale with bounds × w. A supply density inflated by an anisotropic transform (FR-019, projected onto
        // the most-detailed axis) can size ceil(bounds × w) past the GPU limit (un-allocatable) or into the
        // multi-GiB range. Clamp w to the actual bounds so the effect degrades on the densest axis instead of
        // crashing. Inert (returns w) for non-pathological bounds, so byte-identity at w == 1 is preserved.
        Rect bounds = context.CalculateBounds();
        workingScale = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, workingScale);

        using var feContext = new FilterEffectContext(bounds, context.OutputScale, workingScale);
        FilterEffect.Value.Resource.GetOriginal().ApplyTo(feContext, FilterEffect.Value.Resource);
        var effectTargets = new EffectTargets();
        effectTargets.AddRange(context.Input.Select(i => new EffectTarget(i)));

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(
                   effectTargets, builder, context.OutputScale, workingScale, context.MaxWorkingScale))
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
                        },
                        // feature 003 (FR-004/FR-019b): a flushed buffer is a concrete bitmap at the working
                        // density it was rasterized at; report its true At(w) so a parent boundary reconciles
                        // it as bitmap supply, not re-rasterizable Unbounded. Mirrors the else branch below.
                        effectiveScale: t.Scale
                    );
                }).ToArray();
            }
            else
            {
                return activator.CurrentTargets.Select(i =>
                    i.NodeOperation ??
                    RenderNodeOperation.CreateFromRenderTarget(i.Bounds, i.Bounds.Position, i.RenderTarget!, i.Scale))
                    .ToArray();
            }
        }
    }
}
