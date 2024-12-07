using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class FilterEffectRenderNode(FilterEffect filterEffect) : ContainerRenderNode
{
    private readonly int _version = filterEffect.Version;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        using var feContext = new FilterEffectContext(context.CalculateBounds());
        feContext.Apply(FilterEffect);
        var effectTargets = new EffectTargets();
        effectTargets.AddRange(context.Input.Select(i => new EffectTarget(i)));

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(effectTargets, builder, context.CanvasFactory))
        {
            activator.Apply(feContext, 0, null);

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
                    );
                }).ToArray();
            }
            else
            {
                return activator.CurrentTargets.Select(i =>
                    i.NodeOperation ??
                    RenderNodeOperation.CreateFromSurface(i.Bounds, i.Bounds.Position, i.Surface!))
                    .ToArray();
            }
        }
    }

    public FilterEffect FilterEffect { get; } = filterEffect;

    public bool Equals(FilterEffect filterEffect)
    {
        return FilterEffect == filterEffect && _version == filterEffect.Version;
    }
}
