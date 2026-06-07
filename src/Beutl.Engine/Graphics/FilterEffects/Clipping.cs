using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Clipping), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Clipping : FilterEffect
{
    public Clipping()
    {
        ScanProperties<Clipping>();
    }

    [Display(Name = nameof(GraphicsStrings.Clipping_Left), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Left { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Clipping_Top), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Top { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Clipping_Right), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Right { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Clipping_Bottom), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Bottom { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Clipping_AutoCenter), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> AutoCenter { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.Clipping_AutoClip), ResourceType = typeof(GraphicsStrings))]
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
        surface.Flush(true, true);
        using var image = surface.Snapshot();
        using var bitmap = image.ToBitmap(BitmapColorType.Alpha8);

        int x0 = bitmap.Width;
        int y0 = bitmap.Height;
        int x1 = 0;
        int y1 = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            var row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (row[x] != 0)
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

    private static void Apply((Thickness thickness, bool autoCenter, bool autoClip) data,
        CustomFilterEffectContext context)
    {
        Thickness originalThickness = data.thickness;
        bool autoCenter = data.autoCenter;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            Thickness thickness = originalThickness;
            var target = context.Targets[i];
            float w = context.WorkingScale;
            var surface = target.RenderTarget!.Value;
            if (data.autoClip)
            {
                // feature 003: FindRect detects content margins in DEVICE px (the ceil(bounds × w) surface); convert
                // to LOGICAL (÷ w) so the logical clip computation + the CreateTarget re-densification stay consistent.
                // (Auto-clip is content-detection-based, so it is best-effort across scales.)
                Thickness detected = FindRectAndReturnThickness(surface);
                thickness += new Thickness(detected.Left / w, detected.Top / w, detected.Right / w, detected.Bottom / w);
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

                // AutoCenter only RELOCATES the cropped window to the center of the original bounds; the
                // content drawn into it is identical to the in-place crop. Both branches therefore blit the
                // SAME kept region of the source — only the target's bounds differ. The crop offset is buffer
                // -relative (the buffer is clipRect-sized either way), so it is the same value for both.
                // (Previously AutoCenter blitted at +centeredRect, which showed the clipped-away corner of the
                // source at the wrong place.) feature 003: source is device-px (point-blit); offset ×= w.
                Rect targetBounds = autoCenter
                    ? originalRect.CenterRect(clipRect).Translate(target.Bounds.Position)
                    : newBounds;
                EffectTarget newTarget = context.CreateTarget(targetBounds);
                using (ImmediateCanvas newCanvas = context.Open(newTarget))
                using (newCanvas.PushTransform(Matrix.CreateTranslation(pointX * w, pointY * w)))
                {
                    newCanvas.Clear();
                    newCanvas.DrawRenderTarget(target.RenderTarget!,
                        new((target.Bounds.X - newBounds.X) * w, (target.Bounds.Y - newBounds.Y) * w));
                }

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
