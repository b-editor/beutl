using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class MeshWarp
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(MeshWarp));

    private static bool HasArea(Rect bounds) => RenderRectValidation.IsFiniteNonNegative(bounds)
        && bounds.Width > 0 && bounds.Height > 0 && float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom);

    private static Point MapPoint(Rect bounds, Point normalized)
        => new(bounds.X + (double)normalized.X * bounds.Width, bounds.Y + (double)normalized.Y * bounds.Height);

    private static Rect TransformBounds((Tessellation Mesh, BoundsCapture? Capture) data, Rect bounds)
    {
        if (bounds.IsInvalid) return bounds;
        if (!HasArea(bounds)) return Rect.Empty;

        var output = new Rect(MapPoint(bounds, data.Mesh.Minimum), MapPoint(bounds, data.Mesh.Maximum));
        return HasArea(output) ? output : Rect.Empty;
    }

    private readonly record struct BoundsCapture(MeshWarp Owner, MediaTime DisplayTime, MediaTime EvaluationTime);

    private static void CaptureOnly(BoundsCapture capture, CustomFilterEffectContext context)
    {
        if (context is { Intent: RenderIntent.Preview, Purpose: RenderRequestPurpose.Frame })
            capture.Owner.CapturePreviewFrame(capture.DisplayTime, context.Targets.CalculateCompleteBounds(), capture.EvaluationTime);
    }

    private static void Apply((Tessellation Mesh, BoundsCapture? Capture) data, CustomFilterEffectContext context)
    {
        Rect sharedBounds = context.Targets.CalculateCompleteBounds();
        if (context is { Intent: RenderIntent.Preview, Purpose: RenderRequestPurpose.Frame }
            && data.Capture is { } capture)
        {
            capture.Owner.CapturePreviewFrame(capture.DisplayTime, sharedBounds, capture.EvaluationTime);
        }

        bool sharedSpace = context.Targets.Count > 1 && HasArea(sharedBounds);
        (float[] Xs, float[] Ys)? sharedGrid = sharedSpace
            ? data.Mesh.CreateSharedSamples(context.Targets, sharedBounds)
            : null;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            if (!target.IsMaterialized) continue;

            Tessellation? tileMesh = data.Mesh;
            Rect warpBounds = target.Bounds;
            if (sharedGrid is { } samples)
            {
                float subLeft = (float)(((double)target.Bounds.X - sharedBounds.X) / sharedBounds.Width);
                float subTop = (float)(((double)target.Bounds.Y - sharedBounds.Y) / sharedBounds.Height);
                float subRight = (float)(((double)target.Bounds.Right - sharedBounds.X) / sharedBounds.Width);
                float subBottom = (float)(((double)target.Bounds.Bottom - sharedBounds.Y) / sharedBounds.Height);
                var subRect = new Rect(subLeft, subTop, subRight - subLeft, subBottom - subTop);
                tileMesh = data.Mesh.CreateSubMesh(subRect, samples.Xs, samples.Ys);
                warpBounds = sharedBounds;
            }

            if (tileMesh is null)
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            Rect bounds = new(MapPoint(warpBounds, tileMesh.Minimum), MapPoint(warpBounds, tileMesh.Maximum));
            if (sharedGrid is not null && float.IsFinite(context.WorkingScale) && context.WorkingScale > 0)
            {
                // Snap split tiles onto a common working-pixel grid so shared triangle edges share identical pixel centers.
                float scale = context.WorkingScale;
                float x0 = MathF.Floor(bounds.X * scale) / scale;
                float y0 = MathF.Floor(bounds.Y * scale) / scale;
                float x1 = MathF.Ceiling(bounds.Right * scale) / scale;
                float y1 = MathF.Ceiling(bounds.Bottom * scale) / scale;
                Rect snapped = new(x0, y0, x1 - x0, y1 - y0);
                if (HasArea(snapped))
                    bounds = snapped;
            }

            if (!HasArea(bounds))
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            // The renderer clips these local bounds after mapping them into the composition's target domain.
            EffectTarget output = context.CreateTarget(bounds);
            try
            {
                if (!output.IsEmpty && Draw(tileMesh, warpBounds, context, target, output))
                    context.Targets[i] = output;
            }
            finally
            {
                if (output.Owner is null)
                    output.Dispose();
            }
        }
    }

    private static bool Draw(
        Tessellation mesh,
        Rect warpBounds,
        CustomFilterEffectContext context,
        EffectTarget target,
        EffectTarget output)
    {
        RenderTarget source = target.RenderTarget!;
        source.PrepareForSampling(
            RenderTargetSamplingIntent.SameContextTextureSampling(output.RenderTarget!.RawValue.Context));
        using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(source.Value);

        RenderPlacement src = target.Placement;
        RenderPlacement dst = output.Placement;
        float srcScale = src.Scale.Value;
        float dstScale = dst.Scale.Value;
        SKSamplingOptions sampling = RasterShaderMapping.SamplingFor(srcScale, dstScale);
        using SKShader? shader = CreateSourceShader(
            snapshot.Image, source.RawValue.Context, target.Bounds, src, sampling, out Rect srcRasterBounds);
        if (shader is null)
        {
            CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(context.Intent, src.DeviceBounds);
            context.RenderTargetLeaseSession?.MarkContentDropped();
            s_logger.LogWarning("Mesh Warp could not sample the source surface; the preview keeps the source pixels.");
            return false;
        }

        var texs = new SKPoint[mesh.Rest.Length];
        var positions = new SKPoint[mesh.Rest.Length];
        Point srcOrigin = target.Bounds.Position - srcRasterBounds.Position;
        Point dstOrigin = warpBounds.Position - dst.RasterBounds.Position;
        for (int i = 0; i < positions.Length; i++)
        {
            texs[i] = ToDevice(mesh.Rest[i], target.Bounds.Size, srcOrigin, srcScale);
            positions[i] = ToDevice(mesh.Warped[i], warpBounds.Size, dstOrigin, dstScale);
        }

        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texs, colors: null, mesh.Indices);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        using ImmediateCanvas canvas = context.Open(output);
        using (canvas.PushDeviceSpace())
        {
            canvas.Clear();
            canvas.Canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        }
        return true;

        static SKPoint ToDevice(Point point, Size size, Point origin, float scale)
            => new((float)((origin.X + (double)point.X * size.Width) * scale),
                (float)((origin.Y + (double)point.Y * size.Height) * scale));
    }

    // Subset to the semantic source footprint when the backing buffer includes an apron so Clamp never bleeds apron pixels.
    private static SKShader? CreateSourceShader(
        SKImage? image,
        GRRecordingContext? recordingContext,
        Rect sourceBounds,
        RenderPlacement src,
        SKSamplingOptions sampling,
        out Rect rasterBounds)
    {
        rasterBounds = src.RasterBounds;
        if (image is null) return null;

        float srcScale = src.Scale.Value;
        var imageBounds = new PixelRect(new PixelSize(image.Width, image.Height));
        (PixelRect semanticDeviceBounds, Rect semanticRasterBounds) = RasterShaderMapping.ResolveSemanticSource(
            sourceBounds, srcScale, src.DeviceBounds, src.RasterBounds);
        var semanticSubset = new PixelRect(
            semanticDeviceBounds.X - src.DeviceBounds.X,
            semanticDeviceBounds.Y - src.DeviceBounds.Y,
            semanticDeviceBounds.Width,
            semanticDeviceBounds.Height);

        if (semanticSubset != imageBounds
            && semanticSubset.Width > 0 && semanticSubset.Height > 0
            && imageBounds.Contains(semanticSubset))
        {
            using SKImage? subset = recordingContext is null
                ? image.Subset(semanticSubset.ToSKRectI())
                : image.Subset(recordingContext, semanticSubset.ToSKRectI());
            if (subset is not null)
            {
                rasterBounds = semanticRasterBounds;
                return subset.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            }
        }

        return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
    }

    private sealed class Tessellation(
        MeshWarpGrid grid,
        float strength,
        MeshWarpInterpolation interpolation,
        int count,
        int indexCount)
    {
        private const float SampleMergeEpsilon = 1e-6f;

        public Point[] Rest { get; } = new Point[count];
        public Point[] Warped { get; } = new Point[count];
        public ushort[] Indices { get; } = new ushort[indexCount];
        public Point Minimum { get; private set; }
        public Point Maximum { get; private set; }

        public (float[] Xs, float[] Ys) CreateSharedSamples(EffectTargets targets, Rect sharedBounds)
        {
            var boundaryXs = new List<float>(targets.Count * 2);
            var boundaryYs = new List<float>(targets.Count * 2);
            for (int i = 0; i < targets.Count; i++)
            {
                EffectTarget target = targets[i];
                if (!target.IsMaterialized) continue;

                boundaryXs.Add((float)(((double)target.Bounds.X - sharedBounds.X) / sharedBounds.Width));
                boundaryXs.Add((float)(((double)target.Bounds.Right - sharedBounds.X) / sharedBounds.Width));
                boundaryYs.Add((float)(((double)target.Bounds.Y - sharedBounds.Y) / sharedBounds.Height));
                boundaryYs.Add((float)(((double)target.Bounds.Bottom - sharedBounds.Y) / sharedBounds.Height));
            }

            float[] uniqueBoundaryXs = SortAndDeduplicate(boundaryXs);
            float[] uniqueBoundaryYs = SortAndDeduplicate(boundaryYs);

            int subdivisions = 8;
            while (subdivisions > 1
                && (long)(grid.Rows * subdivisions + uniqueBoundaryYs.Length + 1)
                    * (grid.Columns * subdivisions + uniqueBoundaryXs.Length + 1) > ushort.MaxValue)
            {
                subdivisions--;
            }

            int globalRows = grid.Rows * subdivisions;
            int globalColumns = grid.Columns * subdivisions;
            return (MergeWithGlobal(uniqueBoundaryXs, globalColumns), MergeWithGlobal(uniqueBoundaryYs, globalRows));
        }

        public Tessellation? CreateSubMesh(Rect normalizedSubRect, float[] sharedXs, float[] sharedYs)
        {
            if (!HasArea(normalizedSubRect)) return null;

            float[] tileXs = SliceRange(sharedXs, normalizedSubRect.X, normalizedSubRect.Right);
            float[] tileYs = SliceRange(sharedYs, normalizedSubRect.Y, normalizedSubRect.Bottom);
            return CreateFromSamples(grid, strength, interpolation, tileXs, tileYs, normalizedSubRect);
        }

        public static Tessellation? Create(
            MeshWarpGrid grid,
            float strength,
            MeshWarpInterpolation interpolation)
        {
            int subdivisions = 8;
            while ((grid.Rows * subdivisions + 1) * (grid.Columns * subdivisions + 1) > ushort.MaxValue)
                subdivisions--;

            int rows = grid.Rows * subdivisions;
            int columns = grid.Columns * subdivisions;
            var xs = new float[columns + 1];
            for (int c = 0; c <= columns; c++)
                xs[c] = c / (float)columns;
            var ys = new float[rows + 1];
            for (int r = 0; r <= rows; r++)
                ys[r] = r / (float)rows;

            return CreateFromSamples(grid, strength, interpolation, xs, ys, normalizedSubRect: null);
        }

        private static Tessellation? CreateFromSamples(
            MeshWarpGrid grid,
            float strength,
            MeshWarpInterpolation interpolation,
            float[] xs,
            float[] ys,
            Rect? normalizedSubRect)
        {
            if (xs.Length < 2 || ys.Length < 2 || (long)xs.Length * ys.Length > ushort.MaxValue)
                return null;

            int rows = ys.Length - 1;
            int columns = xs.Length - 1;
            int stride = columns + 1;
            var result = new Tessellation(grid, strength, interpolation, (rows + 1) * stride, rows * columns * 6);
            float left = float.PositiveInfinity, top = float.PositiveInfinity;
            float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            int nextIndex = 0;

            for (int row = 0; row <= rows; row++)
            {
                float sharedY = ys[row];
                float tileRestY = normalizedSubRect is { } subForY
                    ? row == 0 ? 0f : row == rows ? 1f : Math.Clamp((float)(((double)sharedY - subForY.Y) / subForY.Height), 0f, 1f)
                    : sharedY;

                for (int column = 0; column <= columns; column++)
                {
                    int vertex = row * stride + column;
                    float sharedX = xs[column];
                    float tileRestX = normalizedSubRect is { } subForX
                        ? column == 0 ? 0f : column == columns ? 1f : Math.Clamp((float)(((double)sharedX - subForX.X) / subForX.Width), 0f, 1f)
                        : sharedX;

                    var sharedRest = new Point(sharedX, sharedY);
                    Point warped = MeshWarpGrid.Lerp(sharedRest, grid.Evaluate(sharedRest, interpolation), strength);
                    if (!MeshWarpGrid.IsFinite(warped)) return null;

                    result.Rest[vertex] = new Point(tileRestX, tileRestY);
                    result.Warped[vertex] = warped;
                    left = MathF.Min(left, warped.X);
                    top = MathF.Min(top, warped.Y);
                    right = MathF.Max(right, warped.X);
                    bottom = MathF.Max(bottom, warped.Y);

                    if (row < rows && column < columns)
                    {
                        result.Indices[nextIndex++] = (ushort)vertex;
                        result.Indices[nextIndex++] = (ushort)(vertex + 1);
                        result.Indices[nextIndex++] = (ushort)(vertex + stride);
                        result.Indices[nextIndex++] = (ushort)(vertex + 1);
                        result.Indices[nextIndex++] = (ushort)(vertex + stride + 1);
                        result.Indices[nextIndex++] = (ushort)(vertex + stride);
                    }
                }
            }

            result.Minimum = new Point(left, top);
            result.Maximum = new Point(right, bottom);
            return result;
        }

        private static float[] SortAndDeduplicate(List<float> values)
        {
            if (values.Count == 0) return [];
            values.Sort();
            var result = new List<float>(values.Count) { values[0] };
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] - result[^1] > SampleMergeEpsilon)
                    result.Add(values[i]);
            }

            return result.ToArray();
        }

        private static float[] MergeWithGlobal(float[] boundaries, int globalCells)
        {
            var merged = new List<float>(boundaries.Length + globalCells + 1);
            merged.AddRange(boundaries);
            for (int i = 0; i <= globalCells; i++)
            {
                float sample = i / (float)globalCells;
                if (!ContainsNear(boundaries, sample))
                    merged.Add(sample);
            }

            merged.Sort();
            return merged.ToArray();
        }

        private static bool ContainsNear(float[] sorted, float value)
        {
            int idx = Array.BinarySearch(sorted, value);
            if (idx >= 0) return true;
            int next = ~idx;
            if (next < sorted.Length && MathF.Abs(sorted[next] - value) <= SampleMergeEpsilon) return true;
            if (next > 0 && MathF.Abs(sorted[next - 1] - value) <= SampleMergeEpsilon) return true;
            return false;
        }

        private static float[] SliceRange(float[] shared, float start, float end)
        {
            if (end - start <= SampleMergeEpsilon || shared.Length < 2)
                return [start, end];

            int startIdx = FindNearestIndex(shared, start);
            int endIdx = FindNearestIndex(shared, end);
            if (endIdx <= startIdx)
                return [start, end];

            return shared[startIdx..(endIdx + 1)];
        }

        private static int FindNearestIndex(float[] sorted, float value)
        {
            int idx = Array.BinarySearch(sorted, value);
            if (idx >= 0) return idx;
            int next = ~idx;
            if (next == 0) return 0;
            if (next >= sorted.Length) return sorted.Length - 1;
            return MathF.Abs(sorted[next] - value) < MathF.Abs(value - sorted[next - 1]) ? next : next - 1;
        }
    }

    public new partial class Resource
    {
        private Tessellation? _tessellation;
        private int _tessellationVersion = -1;

        protected override void ApplyTo(FilterEffectContext context)
        {
            BoundsCapture? capture = null;
            if (context.IsPreviewFrame && GetOriginal() is MeshWarp owner)
            {
                MediaTime displayTime = EvaluationContext?.DisplayedTime ?? MediaTime.Zero;
                MediaTime evaluationTime = EvaluationContext?.Time ?? displayTime;
                if (!owner.CapturePreviewFrame(displayTime, context.Bounds, evaluationTime))
                    capture = new BoundsCapture(owner, displayTime, evaluationTime);
            }

            if (!float.IsFinite(Strength) || Strength == 0 || Mesh is null || Mesh.IsIdentity())
            {
                if (capture is { } pendingCapture)
                    context.CustomEffect(pendingCapture, CaptureOnly, static (_, r) => r);
                return;
            }

            if (_tessellationVersion != Version)
            {
                MeshWarpGrid grid = Mesh.Resample(Rows, Columns, Interpolation);
                _tessellation = grid.IsIdentity() ? null : Tessellation.Create(grid, Strength / 100f, Interpolation);
                _tessellationVersion = Version;
            }

            if (_tessellation is { } mesh)
                context.CustomEffect((mesh, capture), Apply, TransformBounds);
            else if (capture is { } pendingCapture)
                context.CustomEffect(pendingCapture, CaptureOnly, static (_, r) => r);
        }
    }
}
