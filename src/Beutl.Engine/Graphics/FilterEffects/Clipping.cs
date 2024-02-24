using System.ComponentModel.DataAnnotations;

using Beutl.Language;

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

    [Display(Name = nameof(Strings.Thickness), ResourceType = typeof(Strings))]
    public Thickness Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect(Thickness, Apply, TransformBounds);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return TransformBounds(Thickness, bounds);
    }

    private Rect TransformBounds(Thickness thickness, Rect rect)
    {
        return rect.Deflate(thickness).Normalize();
    }

    private void Apply(Thickness thickness, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            var surface = target.Surface!.Value;
            Rect originalRect = target.Bounds;
            Rect clipRect = originalRect.Deflate(thickness).Normalize();

            Rect intersect = originalRect.Intersect(clipRect);

            if (intersect.IsEmpty || clipRect.Width == 0 || clipRect.Height == 0)
            {
                context.Targets.RemoveAt(i);
                i--;
            }
            else
            {
                using SKImage skimage = surface.Snapshot(SKRectI.Floor(intersect.ToSKRect()));
                EffectTarget newtarget = context.CreateTarget(clipRect);

                newtarget.Surface?.Value?.Canvas?.DrawImage(skimage, intersect.X - clipRect.X, intersect.Y - clipRect.Y);

                target.Dispose();
                context.Targets[i] = newtarget;
            }
        }
    }
}
