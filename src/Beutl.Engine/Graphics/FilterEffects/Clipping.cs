using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class Clipping : FilterEffect
{
    public static readonly CoreProperty<Thickness> ThicknessProperty;
    private Thickness _thickness;

    static Clipping()
    {
        ThicknessProperty = ConfigureProperty<Thickness, Clipping>(nameof(Thickness))
            .Accessor(o => o.Thickness, (o, v) => o.Thickness = v)
            .Register();

        AffectsRender<Clipping>(ThicknessProperty);
    }

    public Thickness Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Custom(Thickness, Apply, TransformBounds);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return TransformBounds(Thickness, bounds);
    }

    private Rect TransformBounds(Thickness thickness, Rect rect)
    {
        return rect.Deflate(thickness).Normalize();
    }

    private void Apply(Thickness thickness, FilterEffectCustomOperationContext context)
    {
        if (context.Target.Surface?.Value is SKSurface surface)
        {
            Rect originalRect = new(context.Target.Size);
            Rect clipRect = originalRect.Deflate(thickness).Normalize();

            Rect intersect = originalRect.Intersect(clipRect);

            if (intersect.IsEmpty || clipRect.Width == 0 || clipRect.Height == 0)
            {
                context.ReplaceTarget(EffectTarget.Empty);
            }
            else
            {
                using SKImage skimage = surface.Snapshot(SKRectI.Floor(intersect.ToSKRect()));
                using EffectTarget target = context.CreateTarget((int)clipRect.Width, (int)clipRect.Height);

                target.Surface?.Value?.Canvas?.DrawImage(skimage, intersect.X - clipRect.X, intersect.Y - clipRect.Y);

                context.ReplaceTarget(target);
            }
        }
    }
}
