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
        // Backward: the output buffer occupies TargetBounds, but the source is anchored to NewBounds. When AutoCenter
        // re-centers TargetBounds away from NewBounds, an output texel at o maps to input at o + (NewBounds − Target);
        // the offset is zero (identity) only when AutoCenter=false (TargetBounds == NewBounds). An identity backward
        // otherwise mis-claims and crops the upstream by the centering offset (A3).
        Rect inputBounds = builder.Bounds;
        BoundsContract bounds = autoClip
            ? BoundsContract.RenderTime
            : BoundsContract.Create(
                rect => ComputeClip(rect, thickness, autoCenter).TargetBounds,
                rect =>
                {
                    (Rect targetBounds, Rect newBounds, _, _) = ComputeClip(inputBounds, thickness, autoCenter);
                    return rect.Translate(newBounds.Position - targetBounds.Position);
                },
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
        float wOut = session.WorkingScale;
        float wIn = input.Density.IsUnbounded ? 1f : input.Density.Value;

        Thickness effective = thickness;
        if (autoClip)
        {
            using Bitmap snapshot = input.Snapshot();
            if (FindTransparentMargins(snapshot) is not { } detected)
            {
                // No non-transparent input pixels: the clip region is empty, so the pass must yield no downstream
                // operation rather than a full-size transparent target that stays hit-testable.
                session.DiscardOutput();
                return;
            }

            // Detected margins are in the INPUT snapshot's device px, so they convert to logical by the input density.
            effective += new Thickness(detected.Left / wIn, detected.Top / wIn, detected.Right / wIn, detected.Bottom / wIn);
        }

        // With content present but the total margins meeting or crossing (Left + Right >= width, or Top + Bottom >=
        // height) the kept region is empty. The fixed-clip path's empty forward bounds already drop it before render,
        // but AutoClip resolves at render time (its detected margins are only known here), so guard it in the callback:
        // Deflate clamps a collapsed axis to 0, so test the deflated region and discard the output rather than emit a
        // full-size, still-hit-testable transparent op.
        Rect clipRegion = input.Bounds.WithX(0).WithY(0).Deflate(effective);
        if (clipRegion.Width <= 0 || clipRegion.Height <= 0)
        {
            session.DiscardOutput();
            return;
        }

        // The buffer occupies TargetBounds (already sized by the forward map); the callback only needs the crop
        // offset, which derives from NewBounds (session.Bounds for the render-time AutoClip path).
        (Rect targetBounds, Rect newBounds, float pointX, float pointY) = ComputeClip(input.Bounds, effective, autoCenter);
        Rect reference = autoClip ? session.Bounds : newBounds;

        if (autoClip)
        {
            // The fixed-clip path resolves forward to the clip rect (its buffer is already tight); AutoClip only learns
            // its margins here, so it must request the clipped sub-rect. NewBounds is the region the input is clipped
            // to — the full buffer's other pixels are transparent margins.
            session.SetOutputBounds(newBounds);
        }

        // A downstream deflating pass can ROI-crop the fixed/AutoCenter path so session.Bounds is an OFFSET sub-rect of
        // TargetBounds. The draws below register to session.Bounds' origin, but the source anchor (reference=NewBounds)
        // is authored in the TargetBounds frame; bridge the origin (like FlatShadow) so content lands on the actual
        // buffer. Zero when un-cropped (session.Bounds == TargetBounds) and on the AutoClip path (it sets its own output).
        float bridgeX = autoClip ? 0f : (float)(targetBounds.X - session.Bounds.X) * wOut;
        float bridgeY = autoClip ? 0f : (float)(targetBounds.Y - session.Bounds.Y) * wOut;

        using (canvas.PushDeviceSpace())
        using (bridgeX != 0 || bridgeY != 0 ? canvas.PushTransform(Matrix.CreateTranslation(bridgeX, bridgeY)) : default)
        using (canvas.PushTransform(Matrix.CreateTranslation(pointX * wOut, pointY * wOut)))
        {
            if (autoClip)
            {
                Rect clip = newBounds.Translate(-session.Bounds.Position);
                canvas.Canvas.ClipRect(
                    new SKRect((float)(clip.X * wOut), (float)(clip.Y * wOut),
                        (float)((clip.X + clip.Width) * wOut), (float)((clip.Y + clip.Height) * wOut)));
            }

            // The input buffer exists at wIn; scale it up to the output density before the device-space blit so a
            // carried-down input still fills its kept region at full size. The blit offset is expressed in input px
            // (inside the scale), matching the FlatShadow bridge.
            using (wIn == wOut ? default : canvas.PushTransform(Matrix.CreateScale(wOut / wIn, wOut / wIn)))
            {
                input.Draw(canvas, new Point((input.Bounds.X - reference.X) * wIn, (input.Bounds.Y - reference.Y) * wIn));
            }
        }
    }

    // Alpha-based transparent-margin detection (the AutoClip helper), reading the input snapshot instead of the
    // legacy surface. Device-pixel margins; the caller converts to logical by dividing by the working scale.
    // Returns null when the input has no non-transparent pixel, so the caller can drop the empty clip result.
    private static Thickness? FindTransparentMargins(Bitmap bitmap)
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
            : null;
    }
}
