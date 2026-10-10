using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

// Draws one frame of a clip boundary transition from the evaluated content of both sides.
internal readonly struct TransitionDrawing(
    GraphicsContext2D graphics,
    IReadOnlyList<Drawable.Resource> from,
    IReadOnlyList<Drawable.Resource> to,
    float progress)
{
    // A wipe edge of zero width still gets a one-unit ramp so it stays antialiased.
    private const float WipeEdgeWidth = 1;

    // An iris mask stays opaque to this fraction of its radius and ramps to clear at the rim, which keeps
    // the rim antialiased without softening it much as the circle grows.
    internal const float IrisSolidFraction = 0.995f;

    // How far a cross zoom magnifies: the outgoing clip zooms in to this scale and the incoming clip
    // settles from it.
    private const float ZoomScale = 2;

    public static Vector GetDirection(ClipTransitionDirection direction)
    {
        return direction switch
        {
            ClipTransitionDirection.RightToLeft => new Vector(-1, 0),
            ClipTransitionDirection.TopToBottom => new Vector(0, 1),
            ClipTransitionDirection.BottomToTop => new Vector(0, -1),
            _ => new Vector(1, 0),
        };
    }

    public void DrawCrossDissolve()
    {
        DrawWeighted(1 - progress, progress);
    }

    // Summing premultiplied colour keeps two opaque clips opaque whenever the weights add up to one;
    // drawing one over the other would let the layers beneath show through the middle.
    private void DrawWeighted(float fromWeight, float toWeight)
    {
        using (PushIsolation())
        {
            DrawSide(from, fromWeight, BlendMode.SrcOver);
            DrawSide(to, toWeight, BlendMode.Plus);
        }
    }

    // The outgoing clip gives way to the fill over the first half and the fill to the incoming clip over
    // the second; without a fill the layers beneath show through at the midpoint.
    public void DrawThrough(Brush.Resource? fill)
    {
        bool firstHalf = progress < 0.5f;
        float clipWeight = firstHalf ? 1 - (progress * 2) : (progress * 2) - 1;

        using (PushIsolation())
        {
            DrawSide(firstHalf ? from : to, clipWeight, BlendMode.SrcOver);

            if (fill != null && clipWeight < 1)
            {
                using (graphics.PushBlendMode(BlendMode.Plus))
                using (graphics.PushOpacity(1 - clipWeight))
                {
                    graphics.DrawRectangle(new Rect(graphics.Size), fill, null);
                }
            }
        }
    }

    // The mask ramps across a square the width of the edge, centred on it, and pads outside it: behind
    // the edge it is opaque and ahead of it clear. The edge starts with its ramp ahead of the frame and
    // ends with it behind.
    public void DrawWipe(Brush.Resource mask, ClipTransitionDirection direction)
    {
        var bounds = new Rect(graphics.Size);
        Vector vector = GetDirection(direction);
        float extent = MathF.Abs(bounds.Width * vector.X) + MathF.Abs(bounds.Height * vector.Y);
        Point center = bounds.Center + vector * ((progress - 0.5f) * (extent + WipeEdgeWidth));
        DrawMasked(mask, new Rect(
            center.X - WipeEdgeWidth / 2,
            center.Y - WipeEdgeWidth / 2,
            WipeEdgeWidth,
            WipeEdgeWidth));
    }

    // The mask is a disc filling the square it is drawn into, which grows from the centre of the frame
    // until the disc's rim is past the corners.
    public void DrawIris(Brush.Resource mask)
    {
        var bounds = new Rect(graphics.Size);
        float halfDiagonal = MathF.Sqrt((bounds.Width * bounds.Width) + (bounds.Height * bounds.Height)) / 2;
        float radius = progress * (halfDiagonal + 1) / IrisSolidFraction;
        if (radius <= 0)
        {
            DrawWeighted(1, 0);
            return;
        }

        Point center = bounds.Center;
        DrawMasked(mask, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
    }

    // The incoming clip shows inside a band through the centre of the frame that widens until it fills
    // the frame, and the outgoing clip outside it.
    public void DrawSplit(ClipTransitionOrientation orientation)
    {
        var bounds = new Rect(graphics.Size);
        Rect band = orientation == ClipTransitionOrientation.Vertical
            ? new Rect(bounds.X, bounds.Center.Y - (bounds.Height * progress / 2), bounds.Width, bounds.Height * progress)
            : new Rect(bounds.Center.X - (bounds.Width * progress / 2), bounds.Y, bounds.Width * progress, bounds.Height);

        using (PushIsolation())
        {
            if (from.Count > 0)
            {
                using (graphics.PushClip(band, ClipOperation.Difference))
                {
                    DrawIsolated(from);
                }
            }

            if (to.Count > 0)
            {
                using (graphics.PushBlendMode(BlendMode.Plus))
                using (graphics.PushClip(band))
                {
                    DrawIsolated(to);
                }
            }
        }
    }

    // Both clips move together, the incoming one entering the frame as the outgoing one leaves it.
    public void DrawPush(ClipTransitionDirection direction)
    {
        Vector travel = GetTravel(direction);
        using (PushIsolation())
        {
            DrawPicture(from, Matrix.CreateTranslation(travel * progress), 1, BlendMode.SrcOver);
            DrawPicture(to, Matrix.CreateTranslation(travel * (progress - 1)), 1, BlendMode.Plus);
        }
    }

    // The incoming clip enters the frame over the outgoing clip, which stays where it is.
    public void DrawSlide(ClipTransitionDirection direction)
    {
        Vector travel = GetTravel(direction);
        using (PushIsolation())
        {
            DrawSide(from, 1, BlendMode.SrcOver);
            DrawPicture(to, Matrix.CreateTranslation(travel * (progress - 1)), 1, BlendMode.SrcOver);
        }
    }

    // The outgoing clip zooms in about the centre of the frame while the incoming clip zooms back out
    // from the same scale, the two dissolving into each other.
    public void DrawZoom()
    {
        using (PushIsolation())
        {
            DrawPicture(from, ScaleAboutCenter(1 + ((ZoomScale - 1) * progress)), 1 - progress, BlendMode.SrcOver);
            DrawPicture(to, ScaleAboutCenter(ZoomScale - ((ZoomScale - 1) * progress)), progress, BlendMode.Plus);
        }
    }

    // The incoming clip shows where the mask is opaque and the outgoing clip where it is clear; the two
    // are summed, so along a soft edge they still add up to the whole frame.
    private void DrawMasked(Brush.Resource mask, Rect maskBounds)
    {
        using (PushIsolation())
        {
            if (from.Count > 0)
            {
                using (graphics.PushOpacityMask(mask, maskBounds, invert: true))
                {
                    DrawIsolated(from);
                }
            }

            if (to.Count > 0)
            {
                using (graphics.PushBlendMode(BlendMode.Plus))
                using (graphics.PushOpacityMask(mask, maskBounds))
                {
                    DrawIsolated(to);
                }
            }
        }
    }

    // The distance a clip moves to cross the frame in direction.
    private Vector GetTravel(ClipTransitionDirection direction)
    {
        Vector vector = GetDirection(direction);
        return vector * (MathF.Abs(graphics.Size.Width * vector.X) + MathF.Abs(graphics.Size.Height * vector.Y));
    }

    private Matrix ScaleAboutCenter(float scale)
    {
        Point center = new Rect(graphics.Size).Center;
        return Matrix.CreateTranslation(-center.X, -center.Y)
               * Matrix.CreateScale(scale, scale)
               * Matrix.CreateTranslation(center.X, center.Y);
    }

    // Draws a clip transformed as a picture: what lies outside the frame stays out of it as it moves.
    private void DrawPicture(IReadOnlyList<Drawable.Resource> drawables, Matrix transform, float weight, BlendMode blendMode)
    {
        if (drawables.Count == 0 || weight <= 0) return;

        using (graphics.PushBlendMode(blendMode))
        using (graphics.PushOpacity(Math.Min(weight, 1)))
        using (graphics.PushTransform(transform))
        using (graphics.PushClip(new Rect(graphics.Size)))
        {
            DrawIsolated(drawables);
        }
    }

    private void DrawSide(IReadOnlyList<Drawable.Resource> drawables, float weight, BlendMode blendMode)
    {
        if (drawables.Count == 0 || weight <= 0) return;

        using (graphics.PushBlendMode(blendMode))
        using (graphics.PushOpacity(Math.Min(weight, 1)))
        {
            DrawIsolated(drawables);
        }
    }

    // One isolated group per clip, so the opacity or mask around it applies to the clip as a whole rather
    // than to each of its drawables.
    private void DrawIsolated(IReadOnlyList<Drawable.Resource> drawables)
    {
        using (PushIsolation())
        {
            foreach (Drawable.Resource drawable in drawables)
            {
                graphics.DrawDrawable(drawable);
            }
        }
    }

    // Composites everything inside it into its own layer first, so the Plus blend combines the two clips
    // with each other and not with the layers beneath.
    private PushedState PushIsolation()
    {
        return graphics.PushNode(
            true,
            static isolates => new DrawableGroup.ContentIsolationRenderNode(isolates),
            static (node, isolates) => node.Update(isolates));
    }
}
