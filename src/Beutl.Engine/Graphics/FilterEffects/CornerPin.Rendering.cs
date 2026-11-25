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
        bool finite = float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13)
            && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23)
            && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33);
        if (!finite) return false;

        // The large translation in an offset input can cancel a positive divisor after rounding.
        ReadOnlySpan<Point> inputCorners = [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
        foreach (Point corner in inputCorners)
        {
            float divisor = matrix.GetTransformDivisor(corner);
            if (!float.IsFinite(divisor) || divisor <= 0) return false;
        }

        // The quad has already passed the degeneracy checks. Check that rounding to float preserved the
        // matrix's rank without imposing an absolute determinant cutoff on a valid minification.
        double matrixDeterminant = matrix.M11 * ((double)matrix.M22 * matrix.M33 - (double)matrix.M23 * matrix.M32)
            - matrix.M12 * ((double)matrix.M21 * matrix.M33 - (double)matrix.M23 * matrix.M31)
            + matrix.M13 * ((double)matrix.M21 * matrix.M32 - (double)matrix.M22 * matrix.M31);
        return matrixDeterminant != 0;
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
        var outputBounds = new Rect[context.Targets.Count];
        for (int i = 0; i < outputBounds.Length; i++)
        {
            EffectTarget target = context.Targets[i];
            if (!target.IsMaterialized || !EffectEditingFrames.HasArea(target.Bounds)) continue;

            Rect projected = sharedSpace
                ? target.Bounds.TransformToDeliveredAABB(homography, context.TargetDomain)
                : quadBounds;
            if (EffectEditingFrames.HasArea(projected))
                outputBounds[i] = projected;
        }

        float scale = sharedSpace ? ResolveTileScale(context, outputBounds) : context.WorkingScale;
        using var replacements = new EffectTargets();
        for (int i = 0; i < outputBounds.Length; i++)
        {
            Rect projected = outputBounds[i];
            if (projected.IsEmpty) continue;

            EffectTarget target = context.Targets[i];
            // Allocate on the device grid while keeping the projected bounds for subsequent effects.
            EffectTarget output = context.CreateTarget(projected, target, deviceAlignedScale: scale);
            replacements.Add(output);
            if (output.IsEmpty || !Draw(context, target, output, homography, sharedBounds, tiles))
                return;
        }

        // The preview keeps the whole original group if allocating or sampling any replacement fails.
        context.Targets.Clear();
        context.Targets.AddRange(replacements);
    }

    private static float ResolveTileScale(CustomFilterEffectContext context, Rect[] outputBounds)
    {
        BufferDimensionBudget budget = context.Budget;
        Vector offset = context.DeviceGridOffset;
        float scale = context.WorkingScale;
        float previous;
        do
        {
            previous = scale;
            foreach (Rect bounds in outputBounds)
            {
                if (!bounds.IsEmpty)
                    scale = budget.ClampWorkingScaleToExactFootprint(bounds.Translate(offset), scale);
            }
        }
        // A reduced density changes pixel phase. Recheck earlier tiles on that same grid.
        while (scale < previous);
        return scale;
    }

    private static void DrawTile(SKCanvas canvas, SKPaint paint, Rect bounds, Rect shared,
        (Rect[] ByTop, Rect[] ByLeft)? tiles, Matrix homography, float density)
    {
        if (tiles is not { } ordered)
        {
            canvas.DrawRect(bounds.ToSKRect(), paint);
            return;
        }

        float toleranceX = MathF.Min(shared.Width * 1e-6f, bounds.Width * 0.01f);
        float toleranceY = MathF.Min(shared.Height * 1e-6f, bounds.Height * 0.01f);
        Rect margin = shared.Inflate(new Thickness(shared.Width, shared.Height));
        List<(Rect Draw, Rect Clip)>? partialEdges = null;
        bool left = FindSharedEdge(ordered.ByTop, bounds.Left, bounds.Right, bounds.Top, bounds.Bottom,
            shared.Left, margin.Left, vertical: true, leading: true);
        bool right = FindSharedEdge(ordered.ByTop, bounds.Right, bounds.Left, bounds.Top, bounds.Bottom,
            shared.Right, margin.Right, vertical: true, leading: false);
        bool top = FindSharedEdge(ordered.ByLeft, bounds.Top, bounds.Bottom, bounds.Left, bounds.Right,
            shared.Top, margin.Top, vertical: false, leading: true);
        bool bottom = FindSharedEdge(ordered.ByLeft, bounds.Bottom, bounds.Top, bounds.Left, bounds.Right,
            shared.Bottom, margin.Bottom, vertical: false, leading: false);

        // Extend the clamped shader over shared edges and clip at those edges without antialiasing, so a
        // pixel center belongs to exactly one tile. The draw keeps antialiasing where the edges are exposed.
        Rect draw = bounds;
        if (left || right || top || bottom)
        {
            draw = new Rect(new Point(left ? shared.Left : bounds.Left, top ? shared.Top : bounds.Top),
                new Point(right ? shared.Right : bounds.Right, bottom ? shared.Bottom : bounds.Bottom));
            Rect clip = new(
                new Point(left ? OffsetSeam(bounds.Left, vertical: true) : margin.Left,
                    top ? OffsetSeam(bounds.Top, vertical: false) : margin.Top),
                new Point(right ? OffsetSeam(bounds.Right, vertical: true) : margin.Right,
                    bottom ? OffsetSeam(bounds.Bottom, vertical: false) : margin.Bottom));
            canvas.ClipRect(clip.ToSKRect(), SKClipOperation.Intersect, antialias: false);
        }
        if (partialEdges is null)
        {
            canvas.DrawRect(draw.ToSKRect(), paint);
            return;
        }

        using var builder = new SKPathBuilder();
        builder.AddRect(draw.ToSKRect());
        foreach (var (extension, clip) in partialEdges)
        {
            builder.AddRect(extension.ToSKRect());
            canvas.ClipRect(clip.ToSKRect(), SKClipOperation.Difference, antialias: false);
        }
        using SKPath path = builder.Detach();
        canvas.DrawPath(path, paint);

        bool FindSharedEdge(Rect[] neighbors, float edge, float opposite, float start, float end,
            float extensionEnd, float clipEnd, bool vertical, bool leading)
        {
            float edgeTolerance = vertical ? toleranceX : toleranceY;
            float spanTolerance = vertical ? toleranceY : toleranceX;
            float spanStart = start;
            float spanEnd = start;
            foreach (Rect neighbor in neighbors)
            {
                float neighborStart = vertical ? neighbor.Top : neighbor.Left;
                if (neighborStart >= end) break;

                float neighborEdge = vertical
                    ? (leading ? neighbor.Right : neighbor.Left)
                    : (leading ? neighbor.Bottom : neighbor.Top);
                if (MathF.Abs(neighborEdge - edge) > edgeTolerance) continue;

                float neighborEnd = MathF.Min(end, vertical ? neighbor.Bottom : neighbor.Right);
                if (neighborEnd <= start) continue;

                // Merge adjoining spans while keeping the gaps between disjoint neighbors exposed.
                if (neighborStart > spanEnd + spanTolerance)
                {
                    AddPartialSpan();
                    spanStart = neighborStart;
                }
                spanEnd = MathF.Max(spanEnd, neighborEnd);
                if (spanStart == start && spanEnd >= end - spanTolerance) return true;
            }

            AddPartialSpan();
            return false;

            void AddPartialSpan()
            {
                if (spanEnd <= spanStart) return;
                Rect extension = Strip(opposite, extensionEnd);
                Rect clip = Strip(OffsetSeam(edge, vertical), clipEnd);
                (partialEdges ??= []).Add((extension, clip));
            }

            Rect Strip(float from, float to)
            {
                float min = MathF.Min(from, to);
                float max = MathF.Max(from, to);
                return vertical
                    ? new Rect(new Point(min, spanStart), new Point(max, spanEnd))
                    : new Rect(new Point(spanStart, min), new Point(spanEnd, max));
            }
        }

        float OffsetSeam(float edge, bool vertical)
        {
            // GPU clips can include the pixel center on both sides of a tie. Move both copies of
            // the edge a small fraction of a pixel toward the positive input axis to break the tie.
            Point point = vertical ? new Point(edge, shared.Center.Y) : new Point(shared.Center.X, edge);
            Point projected = homography.Transform(point);
            Vector dx = new(homography.M11 - projected.X * homography.M13, homography.M12 - projected.Y * homography.M13);
            Vector dy = new(homography.M21 - projected.X * homography.M23, homography.M22 - projected.Y * homography.M23);
            float tangent = vertical ? dy.Length : dx.Length;
            float cross = MathF.Abs(dx.X * dy.Y - dx.Y * dy.X);
            float shift = tangent * homography.GetTransformDivisor(point) / (64 * cross * density);
            float maxShift = (vertical ? shared.Width : shared.Height) * 0.01f;
            return float.IsFinite(shift) ? edge + MathF.Min(shift, maxShift) : edge;
        }
    }

    private static bool Draw(CustomFilterEffectContext context, EffectTarget target, EffectTarget output, Matrix homography,
        Rect shared, (Rect[] ByTop, Rect[] ByLeft)? tiles)
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
                DrawTile(canvas.Canvas, paint, target.Bounds, shared, tiles, homography, dstScale);
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
