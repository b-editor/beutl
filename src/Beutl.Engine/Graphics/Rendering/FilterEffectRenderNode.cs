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

        // Resolve working scale from the densest concrete input, capped by the global ceiling.
        Span<EffectiveScale> inputScales = context.Input.Length <= 16
            ? stackalloc EffectiveScale[context.Input.Length]
            : new EffectiveScale[context.Input.Length];
        for (int i = 0; i < context.Input.Length; i++)
        {
            inputScales[i] = context.Input[i].EffectiveScale;
        }

        float workingScale = RenderNodeContext.ResolveWorkingScale(
            inputScales, context.OutputScale, context.MaxWorkingScale);

        // Clamp w to keep ceil(bounds * w) within GPU/memory limits.
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
