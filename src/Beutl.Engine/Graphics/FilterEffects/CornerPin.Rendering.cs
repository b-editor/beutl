using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class CornerPin
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(CornerPin));
    private static readonly SKSamplingOptions s_sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static Point ResolveCorner(Rect bounds, RelativePoint corner)
        => bounds.Position + corner.ToPixels(bounds.Size);

    private static Corners<Point> ResolveCorners(Rect bounds, Corners<RelativePoint> corners)
        => new(ResolveCorner(bounds, corners.TopLeft), ResolveCorner(bounds, corners.TopRight),
            ResolveCorner(bounds, corners.BottomRight), ResolveCorner(bounds, corners.BottomLeft));

    private static bool IsIdentity(Rect bounds, Corners<Point> corners)
        => corners == new Corners<Point>(bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft);

    private static bool IsFinite(Point point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    internal static bool TryCreateHomography(Rect bounds, Point topLeft, Point topRight, Point bottomRight,
        Point bottomLeft, out Matrix matrix)
    {
        matrix = default;
        if (!EffectEditingFrames.HasArea(bounds)
            || !IsFinite(topLeft) || !IsFinite(topRight) || !IsFinite(bottomRight) || !IsFinite(bottomLeft))
            return false;

        double d1x = (double)topRight.X - bottomRight.X;
        double d1y = (double)topRight.Y - bottomRight.Y;
        double d2x = (double)bottomLeft.X - bottomRight.X;
        double d2y = (double)bottomLeft.Y - bottomRight.Y;
        double sumX = (double)topLeft.X - topRight.X - d2x;
        double sumY = (double)topLeft.Y - topRight.Y - d2y;
        double determinant = d1x * d2y - d1y * d2x;
        double edgeScale = Math.Max(Math.Abs(d1x), Math.Abs(d1y)) * Math.Max(Math.Abs(d2x), Math.Abs(d2y));
        if (Math.Abs(determinant) <= 1e-12 * edgeScale) return false;

        double g = (sumX * d2y - sumY * d2x) / determinant;
        double h = (d1x * sumY - d1y * sumX) / determinant;
        double topRightWeight = 1 + g;
        double bottomRightWeight = 1 + g + h;
        double bottomLeftWeight = 1 + h;
        // A positive divisor throughout the input keeps the quad strictly convex in either winding.
        // Comparing against the largest corner weight keeps the degeneracy cutoff symmetric across all four corners.
        double minWeight = Math.Min(Math.Min(1, topRightWeight), Math.Min(bottomRightWeight, bottomLeftWeight));
        double maxWeight = Math.Max(Math.Max(1, topRightWeight), Math.Max(bottomRightWeight, bottomLeftWeight));
        if (minWeight <= 1e-4 * maxWeight) return false;

        // Compose the input-to-unit-square transform in double before storing Beutl's row-vector matrix.
        double xx = (topRightWeight * topRight.X - topLeft.X) / bounds.Width;
        double xy = (topRightWeight * topRight.Y - topLeft.Y) / bounds.Width;
        double xw = g / bounds.Width;
        double yx = (bottomLeftWeight * bottomLeft.X - topLeft.X) / bounds.Height;
        double yy = (bottomLeftWeight * bottomLeft.Y - topLeft.Y) / bounds.Height;
        double yw = h / bounds.Height;
        double tx = topLeft.X - bounds.X * xx - bounds.Y * yx;
        double ty = topLeft.Y - bounds.X * xy - bounds.Y * yy;
        double tw = 1 - bounds.X * xw - bounds.Y * yw;
        // The divisor has an arbitrary common scale. Keep all tile corners in front of the bounds near plane
        // without letting large corner weights lose precision in float32.
        double scale = minWeight < Rect.DefaultNearPlane || maxWeight > 1e4 ? 1 / minWeight : 1;
        matrix = new Matrix(
            (float)(xx * scale), (float)(xy * scale), (float)(xw * scale),
            (float)(yx * scale), (float)(yy * scale), (float)(yw * scale),
            (float)(tx * scale), (float)(ty * scale), (float)(tw * scale));
        return float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13)
            && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23)
            && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33)
            && float.IsFinite(matrix.GetDeterminant()) && matrix.HasInverse;
    }

    private static bool TryCreateHomography(Rect bounds, Corners<Point> corners, out Matrix matrix)
        => TryCreateHomography(bounds, corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft, out matrix);

    private static Rect BoundsOf(Corners<Point> corners)
    {
        (Point tl, Point tr, Point br, Point bl) = corners;
        return new Rect(
            new Point(MathF.Min(MathF.Min(tl.X, tr.X), MathF.Min(br.X, bl.X)),
                MathF.Min(MathF.Min(tl.Y, tr.Y), MathF.Min(br.Y, bl.Y))),
            new Point(MathF.Max(MathF.Max(tl.X, tr.X), MathF.Max(br.X, bl.X)),
                MathF.Max(MathF.Max(tl.Y, tr.Y), MathF.Max(br.Y, bl.Y))));
    }

    private static Rect TransformBounds((Corners<RelativePoint> Corners, BoundsCapture? Capture) data, Rect bounds)
    {
        if (bounds.IsInvalid) return bounds;
        if (!EffectEditingFrames.HasArea(bounds)) return Rect.Empty;

        Corners<Point> corners = ResolveCorners(bounds, data.Corners);
        if (IsIdentity(bounds, corners)) return bounds;
        if (!TryCreateHomography(bounds, corners, out _)) return Rect.Empty;

        Rect warped = BoundsOf(corners);
        return EffectEditingFrames.HasArea(warped) ? warped : Rect.Empty;
    }

    private readonly record struct BoundsCapture(CornerPin Owner, MediaTime DisplayTime, MediaTime EvaluationTime);

    private static void CaptureOnly(BoundsCapture capture, CustomFilterEffectContext context)
    {
        if (context is { Intent: RenderIntent.Preview, Purpose: RenderRequestPurpose.Frame })
            capture.Owner._editingFrames.Capture(capture.DisplayTime, context.Targets.CalculateCompleteBounds(), capture.EvaluationTime);
    }

    private static void Apply((Corners<RelativePoint> Corners, BoundsCapture? Capture) data, CustomFilterEffectContext context)
    {
        Rect sharedBounds = context.Targets.CalculateCompleteBounds();
        if (context is { Intent: RenderIntent.Preview, Purpose: RenderRequestPurpose.Frame } && data.Capture is { } capture)
            capture.Owner._editingFrames.Capture(capture.DisplayTime, sharedBounds, capture.EvaluationTime);

        if (!EffectEditingFrames.HasArea(sharedBounds))
        {
            context.Targets.Clear();
            return;
        }

        Corners<Point> corners = ResolveCorners(sharedBounds, data.Corners);
        if (IsIdentity(sharedBounds, corners)) return;
        if (!TryCreateHomography(sharedBounds, corners, out Matrix homography))
        {
            context.Targets.Clear();
            return;
        }

        Rect quadBounds = BoundsOf(corners);
        if (!EffectEditingFrames.HasArea(quadBounds))
        {
            context.Targets.Clear();
            return;
        }

        bool sharedSpace = context.Targets.Count > 1;
        (Rect[] ByTop, Rect[] ByLeft)? tiles = null;
        if (sharedSpace)
        {
            Rect[] byTop = context.Targets.Where(t => t.IsMaterialized).Select(t => t.Bounds).OrderBy(t => t.Top).ToArray();
            tiles = (byTop, byTop.OrderBy(t => t.Left).ToArray());
        }
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            if (!target.IsMaterialized || !EffectEditingFrames.HasArea(target.Bounds))
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            Rect outputBounds = sharedSpace
                ? target.Bounds.TransformToDeliveredAABB(homography, context.TargetDomain)
                : quadBounds;
            if (!EffectEditingFrames.HasArea(outputBounds))
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            if (sharedSpace)
            {
                // Give all tiles the same sample centers in the output, including at fractional frame origins.
                float scale = context.WorkingScale;
                Vector offset = context.DeviceGridOffset;
                Rect snapped = PixelRect.FromRect(outputBounds.Translate(offset), scale).ToRect(scale).Translate(-offset);
                if (EffectEditingFrames.HasArea(snapped)) outputBounds = snapped;
            }

            EffectTarget output = context.CreateTarget(outputBounds, target, coverEdgePixels: true);
            try
            {
                if (output.IsEmpty) continue;

                (Rect drawBounds, Rect? clip) = GetTileDrawBounds(target.Bounds, sharedBounds, tiles);
                if (Draw(context, target, output, homography, drawBounds, clip))
                    context.Targets[i] = output;
            }
            finally
            {
                if (output.Owner is null)
                    output.Dispose();
            }
        }
    }

    private static (Rect Draw, Rect? Clip) GetTileDrawBounds(Rect bounds, Rect shared, (Rect[] ByTop, Rect[] ByLeft)? tiles)
    {
        if (tiles is not { } ordered) return (bounds, null);

        float toleranceX = MathF.Min(shared.Width * 1e-6f, bounds.Width * 0.01f);
        float toleranceY = MathF.Min(shared.Height * 1e-6f, bounds.Height * 0.01f);
        bool left = IsCovered(ordered.ByTop, bounds.Left, bounds.Top, bounds.Bottom, vertical: true, leading: true);
        bool right = IsCovered(ordered.ByTop, bounds.Right, bounds.Top, bounds.Bottom, vertical: true, leading: false);
        bool top = IsCovered(ordered.ByLeft, bounds.Top, bounds.Left, bounds.Right, vertical: false, leading: true);
        bool bottom = IsCovered(ordered.ByLeft, bounds.Bottom, bounds.Left, bounds.Right, vertical: false, leading: false);

        if (!(left || top || right || bottom)) return (bounds, null);

        // Adjacent tiles must assign a sample to exactly one side of a shared edge. Extend the clamped shader
        // across those edges and use a hard clip at the seam. Exposed edges keep the draw's antialiasing.
        Rect draw = new(new Point(left ? shared.Left : bounds.Left, top ? shared.Top : bounds.Top),
            new Point(right ? shared.Right : bounds.Right, bottom ? shared.Bottom : bounds.Bottom));
        Rect margin = shared.Inflate(new Thickness(shared.Width, shared.Height));
        Rect clip = new(new Point(left ? bounds.Left : margin.Left, top ? bounds.Top : margin.Top),
            new Point(right ? bounds.Right : margin.Right, bottom ? bounds.Bottom : margin.Bottom));
        return (draw, clip);

        // Several shorter tiles can meet the same edge. Extend the covered interval in order without spanning gaps.
        bool IsCovered(Rect[] neighbors, float edge, float start, float end, bool vertical, bool leading)
        {
            float edgeTolerance = vertical ? toleranceX : toleranceY;
            float spanTolerance = vertical ? toleranceY : toleranceX;
            float covered = start;
            foreach (Rect neighbor in neighbors)
            {
                float neighborStart = vertical ? neighbor.Top : neighbor.Left;
                if (neighborStart > covered + spanTolerance) break;

                float neighborEdge = vertical
                    ? (leading ? neighbor.Right : neighbor.Left)
                    : (leading ? neighbor.Bottom : neighbor.Top);
                if (MathF.Abs(neighborEdge - edge) > edgeTolerance) continue;

                covered = MathF.Max(covered, vertical ? neighbor.Bottom : neighbor.Right);
                if (covered >= end - spanTolerance) return true;
            }
            return false;
        }
    }

    private static bool Draw(CustomFilterEffectContext context, EffectTarget target, EffectTarget output, Matrix homography,
        Rect drawBounds, Rect? clip)
    {
        RenderTarget source = target.RenderTarget!;
        source.PrepareForSampling(RenderTargetSamplingIntent.SameContextTextureSampling(output.RenderTarget!.RawValue.Context));
        using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(source.Value);
        if (snapshot.Image is not { } image) return ReportSamplingFailure(context, target);

        RenderPlacement src = target.Placement;
        RenderPlacement dst = output.Placement;
        float srcScale = src.Scale.Value;
        float dstScale = dst.Scale.Value;
        var imageBounds = new PixelRect(new PixelSize(image.Width, image.Height));
        (_, PixelRect semanticSubset, Rect semanticRasterBounds) = RasterShaderMapping.ResolveSemanticSubset(
            target.Bounds, srcScale, src.DeviceBounds, src.RasterBounds);
        PixelRect subset = semanticSubset.Intersect(imageBounds);
        if (subset.Width == 0 || subset.Height == 0) return ReportSamplingFailure(context, target);

        using SKImage? cropped = subset == imageBounds ? null : RasterShaderMapping.SubsetImage(image, source.RawValue.Context, subset);
        if (subset != imageBounds && cropped is null) return ReportSamplingFailure(context, target);

        Point srcOrigin = semanticRasterBounds.Position
            + new Point((subset.X - semanticSubset.X) / srcScale, (subset.Y - semanticSubset.Y) / srcScale);
        Point dstOrigin = dst.LogicalOrigin;
        Matrix logicalToDevice = homography
            * Matrix.CreateTranslation(-dstOrigin.X, -dstOrigin.Y) * Matrix.CreateScale(dstScale, dstScale);

        // Clamp the sampler to the semantic subset and draw only the exact input rectangle, so neither the source
        // apron nor the rounding to whole output pixels becomes part of the image.
        SKImage sourceImage = cropped ?? image;
        using SKImage? prefiltered = PrefilterPerspective(sourceImage, target.Bounds, homography, dstScale / srcScale);
        SKImage sampled = prefiltered ?? sourceImage;
        var shaderMatrix = SKMatrix.CreateScaleTranslation(
            sourceImage.Width / (sampled.Width * srcScale), sourceImage.Height / (sampled.Height * srcScale),
            srcOrigin.X, srcOrigin.Y);
        using SKShader shader = sampled.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, s_sampling, shaderMatrix);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        using ImmediateCanvas canvas = context.Open(output);
        using (canvas.PushDeviceSpace())
        {
            canvas.Clear();
            using (canvas.PushTransform(logicalToDevice))
            {
                if (clip is { } tileClip)
                    canvas.Canvas.ClipRect(tileClip.ToSKRect(), SKClipOperation.Intersect, antialias: false);
                canvas.Canvas.DrawRect(drawBounds.ToSKRect(), paint);
            }
        }
        return true;
    }

    private static SKImage? PrefilterPerspective(SKImage image, Rect bounds, Matrix homography, float densityRatio)
    {
        // The raster backend only selects mipmaps when it can decompose the transform into affine scales.
        if (image.IsTextureBacked || !homography.ContainsPerspective()) return null;

        float minWeight = float.PositiveInfinity;
        float maxDerivativeSquared = 0;
        ReadOnlySpan<Point> corners = [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
        foreach (Point corner in corners)
        {
            Point projected = homography.Transform(corner);
            minWeight = MathF.Min(minWeight, homography.GetTransformDivisor(corner));
            // Each derivative numerator is affine in the projected point, so its length is bounded by
            // the largest length at a corner. Divide by the smallest homogeneous weight for a scale bound.
            Vector dx = new(homography.M11 - projected.X * homography.M13, homography.M12 - projected.Y * homography.M13);
            Vector dy = new(homography.M21 - projected.X * homography.M23, homography.M22 - projected.Y * homography.M23);
            maxDerivativeSquared = MathF.Max(maxDerivativeSquared, MathF.Max(dx.SquaredLength, dy.SquaredLength));
        }

        float scale = MathF.Sqrt(maxDerivativeSquared) * densityRatio / minWeight;
        if (!(scale > 0 && scale <= 0.5f)) return null;

        // Prefilter only when the entire tile shrinks. Sampling regions that magnify still use the original texels.
        int width = Math.Max(1, (int)MathF.Ceiling(image.Width * scale));
        int height = Math.Max(1, (int)MathF.Ceiling(image.Height * scale));
        if (width >= image.Width && height >= image.Height) return null;
        using var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(image.Info.WithSize(width, height))) return null;
        using SKPixmap pixels = bitmap.PeekPixels();
        if (!image.ScalePixels(pixels, s_sampling)) return null;
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    private static bool ReportSamplingFailure(CustomFilterEffectContext context, EffectTarget target)
    {
        CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(context.Intent, target.Placement.DeviceBounds);
        context.RenderTargetLeaseSession?.MarkContentDropped();
        s_logger.LogWarning("Could not sample the source surface; the preview keeps the source pixels.");
        return false;
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            BoundsCapture? capture = null;
            if (context.IsPreviewFrame && GetOriginal() is CornerPin owner)
            {
                MediaTime displayTime = EvaluationContext?.DisplayedTime ?? MediaTime.Zero;
                MediaTime evaluationTime = EvaluationContext?.Time ?? displayTime;
                if (!owner._editingFrames.Capture(displayTime, context.Bounds, evaluationTime))
                    capture = new BoundsCapture(owner, displayTime, evaluationTime);
            }

            var corners = new Corners<RelativePoint>(TopLeft, TopRight, BottomRight, BottomLeft);
            if (corners == s_defaultCorners)
            {
                if (capture is { } pendingCapture)
                    context.CustomEffect(pendingCapture, CaptureOnly, static (_, bounds) => bounds,
                        CustomFilterEffectOptions.AcceptsMaterializedTargets);
                return;
            }

            context.CustomEffect((corners, capture), Apply, TransformBounds, CustomFilterEffectOptions.AcceptsMaterializedTargets);
        }
    }
}
