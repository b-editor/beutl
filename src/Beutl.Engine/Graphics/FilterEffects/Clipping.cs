using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
            Rect originalRect = target.Bounds.WithX(0).WithY(0);
            Rect clipRect = originalRect.Deflate(thickness).Normalize();

            Rect intersect = originalRect.Intersect(clipRect);

            if (intersect.IsEmpty || clipRect.Width == 0 || clipRect.Height == 0)
            {
                context.Targets.RemoveAt(i);
                i--;
            }
            else
            {
                // クリッピング機能と領域展開機能をつけたらコードが汚くなりました。
                float pointX = MathF.CopySign(MathF.Ceiling(thickness.Left) - thickness.Left, thickness.Left);
                float pointY = MathF.CopySign(MathF.Ceiling(thickness.Top) - thickness.Top, thickness.Top);

                using SKImage skImage = surface.Snapshot(SKRectI.Floor(intersect.ToSKRect()));

                var newBounds = clipRect
                    .WithX(target.Bounds.X + thickness.Left - pointX)
                    .WithY(target.Bounds.Y + thickness.Top - pointY);
                if (thickness.Left > 0)
                {
                    // "- 1" は謎のオフセットを解消するために必要です。
                    newBounds = newBounds.WithX(target.Bounds.X + thickness.Left + pointX - 1);
                    pointX = 0;
                }

                if (thickness.Top > 0)
                {
                    newBounds = newBounds.WithY(target.Bounds.Y + thickness.Top + pointY - 1);
                    pointY = 0;
                }

                EffectTarget newTarget = context.CreateTarget(newBounds);

                newTarget.Surface?.Value?.Canvas?.DrawImage(
                    skImage,
                    intersect.X - clipRect.X + pointX,
                    intersect.Y - clipRect.Y + pointY);

                Debug.WriteLine(target.Bounds.X + thickness.Left - pointX);

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
