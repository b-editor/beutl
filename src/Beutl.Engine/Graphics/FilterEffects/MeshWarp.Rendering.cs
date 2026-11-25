using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

public sealed partial class MeshWarp
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(MeshWarp));

    private static bool HasArea(Rect bounds) => EffectEditingFrames.HasArea(bounds);

    private static Point MapPoint(Rect bounds, Point normalized)
        => new(bounds.X + (double)normalized.X * bounds.Width, bounds.Y + (double)normalized.Y * bounds.Height);

    private static float Normalize(float value, float start, float length) => (float)(((double)value - start) / length);

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
        Rect[]? tiles = sharedSpace
            ? context.Targets.Where(target => target.IsMaterialized).Select(target => target.Bounds).ToArray()
            : null;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            if (!target.IsMaterialized) continue;

            Tessellation? tileMesh = data.Mesh;
            Rect warpBounds = target.Bounds;
            TriangleWarpRenderer.Sides edges = tiles is null
                ? TriangleWarpRenderer.Sides.All
                : FindContentEdges(target.Bounds, tiles, sharedBounds);
            if (sharedSpace)
            {
                float subLeft = Normalize(target.Bounds.X, sharedBounds.X, sharedBounds.Width);
                float subTop = Normalize(target.Bounds.Y, sharedBounds.Y, sharedBounds.Height);
                float subRight = Normalize(target.Bounds.Right, sharedBounds.X, sharedBounds.Width);
                float subBottom = Normalize(target.Bounds.Bottom, sharedBounds.Y, sharedBounds.Height);
                var subRect = new Rect(subLeft, subTop, subRight - subLeft, subBottom - subTop);
                tileMesh = sharedGrid is { } samples
                    ? data.Mesh.CreateSubMesh(subRect, samples.Xs, samples.Ys)
                    : data.Mesh.CreateGlobalSubMesh(subRect);
                warpBounds = sharedBounds;
            }

            if (tileMesh is null)
            {
                context.Targets.RemoveAt(i--);
                continue;
            }

            Rect bounds = new(MapPoint(warpBounds, tileMesh.Minimum), MapPoint(warpBounds, tileMesh.Maximum));
            if (sharedSpace && float.IsFinite(context.WorkingScale) && context.WorkingScale > 0)
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
            EffectTarget output = context.CreateTarget(bounds, target);
            try
            {
                if (!output.IsEmpty && TriangleWarpRenderer.Draw(
                        warpBounds, tileMesh.Rest, tileMesh.Warped, tileMesh.Indices, tileMesh.GetOutline(edges),
                        context, target, output, s_logger))
                    context.Targets[i] = output;
            }
            finally
            {
                if (output.Owner is null)
                    output.Dispose();
            }
        }
    }

    // A side of a target that another target meets is a seam inside the shared warp, not an edge of its content.
    private static TriangleWarpRenderer.Sides FindContentEdges(Rect bounds, Rect[] tiles, Rect sharedBounds)
    {
        float toleranceX = Tessellation.SampleMergeEpsilon * sharedBounds.Width;
        float toleranceY = Tessellation.SampleMergeEpsilon * sharedBounds.Height;
        TriangleWarpRenderer.Sides edges = TriangleWarpRenderer.Sides.All;
        foreach (Rect tile in tiles)
        {
            bool sharesRows = tile.Top < bounds.Bottom - toleranceY && bounds.Top < tile.Bottom - toleranceY;
            bool sharesColumns = tile.Left < bounds.Right - toleranceX && bounds.Left < tile.Right - toleranceX;
            if (sharesRows && MathF.Abs(tile.Right - bounds.Left) <= toleranceX)
                edges &= ~TriangleWarpRenderer.Sides.Left;
            if (sharesRows && MathF.Abs(tile.Left - bounds.Right) <= toleranceX)
                edges &= ~TriangleWarpRenderer.Sides.Right;
            if (sharesColumns && MathF.Abs(tile.Bottom - bounds.Top) <= toleranceY)
                edges &= ~TriangleWarpRenderer.Sides.Top;
            if (sharesColumns && MathF.Abs(tile.Top - bounds.Bottom) <= toleranceY)
                edges &= ~TriangleWarpRenderer.Sides.Bottom;
        }

        return edges;
    }

    private sealed class Tessellation(
        MeshWarpGrid grid,
        float strength,
        MeshWarpInterpolation interpolation,
        int rows,
        int columns)
    {
        internal const float SampleMergeEpsilon = 1e-6f;
        private readonly int _rows = rows;
        private readonly int _columns = columns;

        public Point[] Rest { get; } = new Point[(rows + 1) * (columns + 1)];
        public Point[] Warped { get; } = new Point[(rows + 1) * (columns + 1)];
        public ushort[] Indices { get; } = new ushort[rows * columns * 6];
        public Point Minimum { get; private set; }
        public Point Maximum { get; private set; }

        // The edges along the given sides of the lattice, each with the third corner of the triangle it belongs to.
        // Each cell is two triangles in row-major order, the first holding its top and left edges.
        public TriangleWarpRenderer.OutlineEdge[] GetOutline(TriangleWarpRenderer.Sides sides)
        {
            int stride = _columns + 1;
            var outline = new List<TriangleWarpRenderer.OutlineEdge>(2 * (_rows + _columns));
            if (sides.HasFlag(TriangleWarpRenderer.Sides.Top))
            {
                for (int column = 0; column < _columns; column++)
                    outline.Add(new(column, column + 1, column + stride, Triangle(0, column, second: false)));
            }

            if (sides.HasFlag(TriangleWarpRenderer.Sides.Right))
            {
                for (int row = 0; row < _rows; row++)
                {
                    int vertex = row * stride + _columns - 1;
                    outline.Add(new(vertex + 1, vertex + stride + 1, vertex + stride,
                        Triangle(row, _columns - 1, second: true)));
                }
            }

            if (sides.HasFlag(TriangleWarpRenderer.Sides.Bottom))
            {
                for (int column = 0; column < _columns; column++)
                {
                    int vertex = (_rows - 1) * stride + column;
                    outline.Add(new(vertex + stride + 1, vertex + stride, vertex + 1,
                        Triangle(_rows - 1, column, second: true)));
                }
            }

            if (sides.HasFlag(TriangleWarpRenderer.Sides.Left))
            {
                for (int row = 0; row < _rows; row++)
                {
                    int vertex = row * stride;
                    outline.Add(new(vertex + stride, vertex, vertex + 1, Triangle(row, 0, second: false)));
                }
            }

            return outline.ToArray();

            int Triangle(int row, int column, bool second) => (row * _columns + column) * 2 + (second ? 1 : 0);
        }

        public (float[] Xs, float[] Ys)? CreateSharedSamples(EffectTargets targets, Rect sharedBounds)
        {
            var boundaryXs = new List<float>(targets.Count * 2);
            var boundaryYs = new List<float>(targets.Count * 2);
            for (int i = 0; i < targets.Count; i++)
            {
                EffectTarget target = targets[i];
                if (!target.IsMaterialized) continue;

                boundaryXs.Add(Normalize(target.Bounds.X, sharedBounds.X, sharedBounds.Width));
                boundaryXs.Add(Normalize(target.Bounds.Right, sharedBounds.X, sharedBounds.Width));
                boundaryYs.Add(Normalize(target.Bounds.Y, sharedBounds.Y, sharedBounds.Height));
                boundaryYs.Add(Normalize(target.Bounds.Bottom, sharedBounds.Y, sharedBounds.Height));
            }

            float[] uniqueBoundaryXs = SortAndDeduplicate(boundaryXs);
            float[] uniqueBoundaryYs = SortAndDeduplicate(boundaryYs);

            int extraRows = uniqueBoundaryYs.Length + 1;
            int extraColumns = uniqueBoundaryXs.Length + 1;
            int subdivisions = FitSubdivisions(grid, extraRows, extraColumns);

            // If even the coarsest shared lattice exceeds the ushort vertex limit (e.g. many PartsSplitEffect contours),
            // let each tile tessellate against the global grid lines plus its own bounds instead of dropping targets.
            if (!FitsIndices(grid, subdivisions, extraRows, extraColumns))
            {
                return null;
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
            if ((long)tileXs.Length * tileYs.Length > ushort.MaxValue)
                return CreateGlobalSubMesh(normalizedSubRect);

            return CreateFromSamples(grid, strength, interpolation, tileXs, tileYs, normalizedSubRect);
        }

        public Tessellation? CreateGlobalSubMesh(Rect normalizedSubRect)
        {
            if (!HasArea(normalizedSubRect)) return null;

            int subdivisions = FitSubdivisions(grid, 3, 3);
            float[] tileXs = SliceRange(
                MergeWithGlobal(SortAndDeduplicate([normalizedSubRect.X, normalizedSubRect.Right]), grid.Columns * subdivisions),
                normalizedSubRect.X, normalizedSubRect.Right);
            float[] tileYs = SliceRange(
                MergeWithGlobal(SortAndDeduplicate([normalizedSubRect.Y, normalizedSubRect.Bottom]), grid.Rows * subdivisions),
                normalizedSubRect.Y, normalizedSubRect.Bottom);
            return CreateFromSamples(grid, strength, interpolation, tileXs, tileYs, normalizedSubRect);
        }

        public static Tessellation? Create(
            MeshWarpGrid grid,
            float strength,
            MeshWarpInterpolation interpolation)
        {
            int subdivisions = FitSubdivisions(grid, 1, 1);
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
            var result = new Tessellation(grid, strength, interpolation, rows, columns);
            float left = float.PositiveInfinity, top = float.PositiveInfinity;
            float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            int nextIndex = 0;

            for (int row = 0; row <= rows; row++)
            {
                float sharedY = ys[row];
                float tileRestY = normalizedSubRect is { } subForY
                    ? row == 0 ? 0f : row == rows ? 1f : Math.Clamp(Normalize(sharedY, subForY.Y, subForY.Height), 0f, 1f)
                    : sharedY;

                for (int column = 0; column <= columns; column++)
                {
                    int vertex = row * stride + column;
                    float sharedX = xs[column];
                    float tileRestX = normalizedSubRect is { } subForX
                        ? column == 0 ? 0f : column == columns ? 1f : Math.Clamp(Normalize(sharedX, subForX.X, subForX.Width), 0f, 1f)
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

        // The finest subdivision per cell, at most 8, whose lattice plus the extra sample lines fits ushort indices.
        private static int FitSubdivisions(MeshWarpGrid grid, int extraRows, int extraColumns)
        {
            int subdivisions = 8;
            while (subdivisions > 1 && !FitsIndices(grid, subdivisions, extraRows, extraColumns))
                subdivisions--;

            return subdivisions;
        }

        private static bool FitsIndices(MeshWarpGrid grid, int subdivisions, int extraRows, int extraColumns)
            => (long)(grid.Rows * subdivisions + extraRows) * (grid.Columns * subdivisions + extraColumns) <= ushort.MaxValue;

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
