using Beutl.Graphics.Effects;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public class FilterEffectNode : ContainerNode
{
    public FilterEffectNode(FilterEffect filterEffect)
    {
        ImageEffect = filterEffect;
    }

    public FilterEffect ImageEffect { get; }

    public bool Equals(FilterEffect imageEffect)
    {
        return ImageEffect == imageEffect;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (var builder = new FilterEffectBuilder())
        using (var target = new EffectTarget(this))
        using (var context = new FilterEffectContext(OriginalBounds, target, builder))
        {
            ImageEffect.ApplyTo(context);

            if (context.Builder.HasFilter())
            {
                using (var paint = new SKPaint())
                {
                    paint.ImageFilter = context.Builder.GetFilter();
                    int count = canvas._canvas.SaveLayer(paint);
                    canvas._canvas.Translate(context.OriginalBounds.X, context.OriginalBounds.Y);

                    context.CurrentTarget.Draw(canvas);

                    canvas._canvas.RestoreToCount(count);
                }
            }
            else if (context.CurrentTarget.Surface != null)
            {
                canvas._canvas.DrawSurface(context.CurrentTarget.Surface.Value, context.Bounds.X, context.Bounds.Y);
            }
            else
            {
                base.Render(canvas);
            }
        }
    }
}
