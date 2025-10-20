using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class Clipping : FilterEffect
{
    public Clipping()
    {
        ScanProperties<Clipping>();
    }

    [Display(Name = nameof(Strings.Left), ResourceType = typeof(Strings))]
    public IProperty<float> Left { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Top), ResourceType = typeof(Strings))]
    public IProperty<float> Top { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Right), ResourceType = typeof(Strings))]
    public IProperty<float> Right { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Bottom), ResourceType = typeof(Strings))]
    public IProperty<float> Bottom { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.AutomaticCentering), ResourceType = typeof(Strings))]
    public IProperty<bool> AutoCenter { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(Strings.ClipTransparentArea), ResourceType = typeof(Strings))]
    public IProperty<bool> AutoClip { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var thickness = new Thickness(r.Left, r.Top, r.Right, r.Bottom);
        context.CustomEffect((thickness, r.AutoCenter, r.AutoClip), Apply, TransformBounds);
    }

    private static Rect TransformBounds((Thickness thickness, bool autoCenter, bool autoClip) data, Rect rect)
    {
        if (data.autoClip) return Rect.Invalid;

        var result = rect.Deflate(data.thickness).Normalize();
        if (data.autoCenter)
        {
            result = rect.CenterRect(result);
        }

        return result;
    }

    private static Thickness FindRectAndReturnThickness(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var bitmap = new Bitmap<Grayscale8>(image.Width, image.Height);
        image.ReadPixels(
            new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Alpha8),
            bitmap.Data,
            bitmap.Width,
            0, 0);

        int x0 = bitmap.Width;
        int y0 = bitmap.Height;
        int x1 = 0;
        int y1 = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap[x, y].Value != 0)
                {
                    if (x0 > x) x0 = x;
                    if (y0 > y) y0 = y;
                    if (x1 < x) x1 = x;
                    if (y1 < y) y1 = y;
                }
            }
        }

        return new Thickness(x0, y0, bitmap.Width - x1, bitmap.Height - y1);
    }

    private static void Apply((Thickness thickness, bool autoCenter, bool autoClip) data, CustomFilterEffectContext context)
    {
        Thickness originalThickness = data.thickness;
        bool autoCenter = data.autoCenter;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            Thickness thickness = originalThickness;
            var target = context.Targets[i];
            var surface = target.RenderTarget!.Value;
            if (data.autoClip)
            {
                thickness += FindRectAndReturnThickness(surface);
            }

            Rect originalRect = target.Bounds.WithX(0).WithY(0);
            Rect clipRect = originalRect.Deflate(thickness).Normalize();
            Rect intersect = originalRect.Intersect(clipRect);

            if (intersect.IsEmpty || clipRect.Width == 0 || clipRect.Height == 0)
            {
                context.Targets.RemoveAt(i);
                target.Dispose();
                i--;
            }
            else
            {
                float pointX = MathF.CopySign(MathF.Ceiling(thickness.Left) - thickness.Left, thickness.Left);
                float pointY = MathF.CopySign(MathF.Ceiling(thickness.Top) - thickness.Top, thickness.Top);

                var newBounds = clipRect
                    .WithX(target.Bounds.X + thickness.Left - pointX)
                    .WithY(target.Bounds.Y + thickness.Top - pointY);
                if (thickness.Left > 0)
                {
                    newBounds = newBounds.WithX(target.Bounds.X + thickness.Left + pointX - 1);
                    pointX = 0;
                }

                if (thickness.Top > 0)
                {
                    newBounds = newBounds.WithY(target.Bounds.Y + thickness.Top + pointY - 1);
                    pointY = 0;
                }

                EffectTarget newTarget;
                if (autoCenter)
                {
                    Rect centeredRect = originalRect.CenterRect(clipRect);
                    newTarget = context.CreateTarget(centeredRect.Translate(target.Bounds.Position));
                    using (ImmediateCanvas newCanvas = context.Open(newTarget))
                    {
                        using (newCanvas.PushTransform(Matrix.CreateTranslation(pointX, pointY)))
                        {
                            newCanvas.DrawRenderTarget(target.RenderTarget!, new(centeredRect.X, centeredRect.Y));
                        }
                    }
                }
                else
                {
                    newTarget = context.CreateTarget(newBounds);
                    using (ImmediateCanvas newCanvas = context.Open(newTarget))
                    {
                        using (newCanvas.PushTransform(Matrix.CreateTranslation(pointX, pointY)))
                        {
                            newCanvas.DrawRenderTarget(target.RenderTarget!, new(target.Bounds.X - newBounds.X, target.Bounds.Y - newBounds.Y));
                        }
                    }
                }

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
