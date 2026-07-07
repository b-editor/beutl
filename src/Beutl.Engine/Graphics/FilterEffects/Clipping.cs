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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var thickness = new Thickness(r.Left, r.Top, r.Right, r.Bottom);
        bool autoCenter = r.AutoCenter;
        bool autoClip = r.AutoClip;

        // autoClip lays out from the input pixels (execution-time), so its bounds are render-time; a fixed clip
        // resolves its output bounds forward from the input rect, exactly as the legacy CustomEffect did.
        BoundsContract bounds = autoClip
            ? BoundsContract.RenderTime
            : BoundsContract.Create(
                rect => ComputeClip(rect, thickness, autoCenter).TargetBounds,
                static r => r,
                isRenderTimeResolved: false);

        builder.Geometry(GeometryNodeDescriptor.Create(
            session => ApplyGeometry(session, thickness, autoCenter, autoClip),
            bounds,
            structuralToken: nameof(Clipping)));
    }

    // Reproduces the legacy Apply layout: the buffer occupies TargetBounds (recentered when AutoCenter), while the
    // source is blitted at an offset derived from NewBounds so the same kept region is drawn either way.
    private static (Rect TargetBounds, Rect NewBounds, float PointX, float PointY) ComputeClip(
        Rect inputBounds, Thickness thickness, bool autoCenter)
    {
        Rect originalRect = inputBounds.WithX(0).WithY(0);
        Rect clipRect = originalRect.Deflate(thickness).Normalize();

        float pointX = MathF.CopySign(MathF.Ceiling(thickness.Left) - thickness.Left, thickness.Left);
        float pointY = MathF.CopySign(MathF.Ceiling(thickness.Top) - thickness.Top, thickness.Top);

        Rect newBounds = clipRect
            .WithX(inputBounds.X + thickness.Left - pointX)
            .WithY(inputBounds.Y + thickness.Top - pointY);
        if (thickness.Left > 0)
        {
            newBounds = newBounds.WithX(inputBounds.X + thickness.Left + pointX - 1);
            pointX = 0;
        }

        if (thickness.Top > 0)
        {
            newBounds = newBounds.WithY(inputBounds.Y + thickness.Top + pointY - 1);
            pointY = 0;
        }

        Rect targetBounds = autoCenter
            ? originalRect.CenterRect(clipRect).Translate(inputBounds.Position)
            : newBounds;
        return (targetBounds, newBounds, pointX, pointY);
    }

    private static void ApplyGeometry(
        GeometrySession session, Thickness thickness, bool autoCenter, bool autoClip)
    {
        EffectInput input = session.Inputs[0];
        ImmediateCanvas canvas = session.OpenCanvas();
        float w = session.WorkingScale;

        Thickness effective = thickness;
        if (autoClip)
        {
            using Bitmap snapshot = input.Snapshot();
            Thickness detected = FindTransparentMargins(snapshot);
            effective += new Thickness(detected.Left / w, detected.Top / w, detected.Right / w, detected.Bottom / w);
        }

        // The buffer occupies TargetBounds (already sized by the forward map); the callback only needs the crop
        // offset, which derives from NewBounds (session.Bounds for the render-time AutoClip path).
        (_, Rect newBounds, float pointX, float pointY) = ComputeClip(input.Bounds, effective, autoCenter);
        Rect reference = autoClip ? session.Bounds : newBounds;

        using (canvas.PushDeviceSpace())
        using (canvas.PushTransform(Matrix.CreateTranslation(pointX * w, pointY * w)))
        {
            if (autoClip)
            {
                Rect clip = newBounds.Translate(-session.Bounds.Position);
                canvas.Canvas.ClipRect(
                    new SKRect((float)(clip.X * w), (float)(clip.Y * w),
                        (float)((clip.X + clip.Width) * w), (float)((clip.Y + clip.Height) * w)));
            }

            input.Draw(canvas, new Point((input.Bounds.X - reference.X) * w, (input.Bounds.Y - reference.Y) * w));
        }
    }

    // Alpha-based transparent-margin detection (the AutoClip helper), reading the input snapshot instead of the
    // legacy surface. Device-pixel margins; the caller converts to logical by dividing by the working scale.
    private static Thickness FindTransparentMargins(Bitmap bitmap)
    {
        int x0 = bitmap.Width, y0 = bitmap.Height, x1 = 0, y1 = 0;
        bool any = false;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.SKBitmap.GetPixel(x, y).Alpha != 0)
                {
                    any = true;
                    if (x0 > x) x0 = x;
                    if (y0 > y) y0 = y;
                    if (x1 < x) x1 = x;
                    if (y1 < y) y1 = y;
                }
            }
        }

        return any
            ? new Thickness(x0, y0, bitmap.Width - x1, bitmap.Height - y1)
            : default;
    }
}
