using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class TriangleWarpRenderer
{
    // Bilinear filtering spreads a source pixel up to half a pixel past an outline along its edge.
    private const float FilterReach = 0.5f;
    // A sharp corner of the outline moves out at most this many margins along its miter.
    private const float MaxMiter = 4;
    // How far inside a side that does not fade every point lies, in output pixels.
    private const float FarInside = -1e4f;
    // A side covers all of a pixel centered this far inside it, in output pixels, and none of one this far outside.
    private const float HalfPixel = 0.5f;
    private static readonly float[] s_fadeEnds = [-HalfPixel, HalfPixel];

    /// <summary>The sides of the source rectangle.</summary>
    [Flags]
    internal enum Sides
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        All = Left | Top | Right | Bottom,
    }

    /// <summary>
    /// An edge of a mesh's outline, the third corner of the triangle it belongs to, and that triangle's position in
    /// draw order.
    /// </summary>
    internal readonly record struct OutlineEdge(int Start, int End, int Opposite, int Triangle);

    public static bool Draw(
        Rect warpBounds,
        ReadOnlySpan<Point> rest,
        ReadOnlySpan<Point> deformed,
        ushort[] indices,
        ReadOnlySpan<OutlineEdge> outline,
        CustomFilterEffectContext context,
        EffectTarget target,
        EffectTarget output,
        ILogger logger)
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
            snapshot.Image, source.RawValue.Context, target.Bounds, src, sampling, out Rect srcRasterBounds,
            out SKSizeI sourceSize);
        if (shader is null)
        {
            CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(context.Intent, src.DeviceBounds);
            context.RenderTargetLeaseSession?.MarkContentDropped();
            logger.LogWarning("Could not sample the source surface; the preview keeps the source pixels.");
            return false;
        }

        var texs = new SKPoint[rest.Length];
        var positions = new SKPoint[rest.Length];
        Point srcOrigin = target.Bounds.Position - srcRasterBounds.Position;
        Point dstOrigin = warpBounds.Position - dst.RasterBounds.Position;
        for (int i = 0; i < positions.Length; i++)
        {
            texs[i] = ToDevice(rest[i], target.Bounds.Size, srcOrigin, srcScale);
            positions[i] = ToDevice(deformed[i], warpBounds.Size, dstOrigin, dstScale);
        }

        // Triangles have no coverage anti-aliasing. The outline moves out onto pixels where the source is
        // transparent, and along a side of the source each triangle fades the source out over one output pixel in its
        // own colors. A triangle drawn later still lays over it, and what a fold lays it over still shows through.
        Mesh mesh = ExtendOutline(texs, positions, indices, outline, sourceSize) switch
        {
            null => new Mesh(texs, positions, null, Array.ConvertAll(indices, static index => (int)index)),
            { Fading: Sides.None } extended => new Mesh(extended.Texs, extended.Positions, null, extended.Indices),
            { } extended => new Fader(extended, sourceSize).Build(),
        };

        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        using ImmediateCanvas canvas = context.Open(output);
        using (canvas.PushDeviceSpace())
        {
            canvas.Clear();
            mesh.Draw(canvas.Canvas, paint);
        }
        return true;

        static SKPoint ToDevice(Point point, Size size, Point origin, float scale)
            => new((float)((origin.X + (double)point.X * size.Width) * scale),
                (float)((origin.Y + (double)point.Y * size.Height) * scale));
    }

    // Moves each corner of the outline out along its miter, as far as the source can still show past it, and joins it
    // to the outline with strips that continue the map of the triangle beside them, drawn right after that triangle.
    // The strips' own aliased edges then fall where the source is transparent, or past a side of the source that fades
    // it out. Null when no corner can move.
    private static Extension? ExtendOutline(
        SKPoint[] texs,
        SKPoint[] positions,
        ushort[] indices,
        ReadOnlySpan<OutlineEdge> outline,
        SKSizeI size)
    {
        if (outline.IsEmpty) return null;

        Sides fading = Sides.None;
        var corners = new Dictionary<int, Corner>(outline.Length);
        foreach (OutlineEdge edge in outline)
        {
            Vector2 start = ToVector(texs[edge.Start]);
            Vector2 end = ToVector(texs[edge.End]);
            Vector2 along = end - start;
            if (along.LengthSquared() == 0) continue;

            var normal = Vector2.Normalize(new Vector2(along.Y, -along.X));
            if (Vector2.Dot(normal, ToVector(texs[edge.Opposite]) - start) > 0)
                normal = -normal;

            Sides sides = SidesAlong(start, end, normal, size);
            fading |= sides;
            bool mapped = TryMap(texs, positions, edge.Start, edge.End, edge.Opposite, out Matrix3x2 toOutput);
            AddCorner(edge.Start);
            AddCorner(edge.End);

            void AddCorner(int vertex)
            {
                ref Corner corner = ref CollectionsMarshal.GetValueRefOrAddDefault(corners, vertex, out _);
                corner.Normals += normal;
                corner.Edges++;
                corner.Sides |= sides;
                if (mapped)
                {
                    corner.ToOutput += toOutput;
                    corner.Maps++;
                }
            }
        }

        int count = texs.Length;
        float reach = 0;
        var moved = new Dictionary<int, int>(corners.Count);
        var movedTexs = new List<SKPoint>(corners.Count);
        var movedPositions = new List<SKPoint>(corners.Count);
        foreach ((int vertex, Corner corner) in corners)
        {
            float normalsSquared = corner.Normals.LengthSquared();
            if (corner.Maps == 0 || normalsSquared < 1e-6f) continue;

            Matrix3x2 toOutput = corner.ToOutput * (1f / corner.Maps);
            float margin = Margin(texs[vertex], corner.Sides, size, toOutput);
            if (margin <= 0) continue;

            // Scaled so that it lies one margin out from both edges at the corner: the miter.
            Vector2 miter = corner.Normals * (corner.Edges / normalsSquared);
            float miterLength = miter.Length();
            if (miterLength > MaxMiter)
                miter *= MaxMiter / miterLength;

            Vector2 offset = miter * margin;
            Vector2 position = ToVector(positions[vertex]) + Vector2.Transform(offset, toOutput);
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) continue;

            if (corner.Sides != Sides.None)
                reach = MathF.Max(reach, margin);
            moved.Add(vertex, count + movedTexs.Count);
            movedTexs.Add(new SKPoint(texs[vertex].X + offset.X, texs[vertex].Y + offset.Y));
            movedPositions.Add(new SKPoint(position.X, position.Y));
        }

        if (moved.Count == 0) return null;

        var strips = new List<(int Triangle, int A, int B, int C)>(outline.Length * 2);
        foreach (OutlineEdge edge in outline)
        {
            int start = moved.GetValueOrDefault(edge.Start, edge.Start);
            int end = moved.GetValueOrDefault(edge.End, edge.End);
            if (end != edge.End)
                strips.Add((edge.Triangle, edge.Start, edge.End, end));
            if (start != edge.Start)
                strips.Add((edge.Triangle, edge.Start, end, start));
        }

        strips.Sort(static (x, y) => x.Triangle.CompareTo(y.Triangle));
        var ordered = new List<int>(indices.Length + strips.Count * 3);
        int next = 0;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            ordered.Add(indices[i]);
            ordered.Add(indices[i + 1]);
            ordered.Add(indices[i + 2]);
            for (; next < strips.Count && strips[next].Triangle <= i / 3; next++)
                AddStrip(strips[next]);
        }

        for (; next < strips.Count; next++)
            AddStrip(strips[next]);

        return new Extension([.. texs, .. movedTexs], [.. positions, .. movedPositions], ordered.ToArray(), fading, reach);

        void AddStrip((int Triangle, int A, int B, int C) strip)
        {
            ordered.Add(strip.A);
            ordered.Add(strip.B);
            ordered.Add(strip.C);
        }
    }

    // The sides of the source an outline edge runs along, facing out of them.
    private static Sides SidesAlong(Vector2 start, Vector2 end, Vector2 normal, SKSizeI size)
    {
        Sides sides = Sides.None;
        if (normal.X < -0.5f && Near(start.X) && Near(end.X)) sides |= Sides.Left;
        if (normal.Y < -0.5f && Near(start.Y) && Near(end.Y)) sides |= Sides.Top;
        if (normal.X > 0.5f && Near(size.Width - start.X) && Near(size.Width - end.X)) sides |= Sides.Right;
        if (normal.Y > 0.5f && Near(size.Height - start.Y) && Near(size.Height - end.Y)) sides |= Sides.Bottom;
        return sides;

        // A source pixel within one pixel of a side holds the content's edge there.
        static bool Near(float distance) => MathF.Abs(distance) < 1;
    }

    // How far past a corner of the outline the source can still show, in source pixels: the reach of its filter, and
    // at a side that fades, the rest of the way to the side and one output pixel past it at the corner's scale.
    private static float Margin(SKPoint point, Sides sides, SKSizeI size, Matrix3x2 toOutput)
    {
        if (!Matrix3x2.Invert(toOutput, out Matrix3x2 toSource)) return 0;

        (float acrossColumns, float acrossRows) = Across(toSource);
        float margin = FilterReach;
        if (sides.HasFlag(Sides.Left)) margin = MathF.Max(margin, point.X + acrossColumns);
        if (sides.HasFlag(Sides.Top)) margin = MathF.Max(margin, point.Y + acrossRows);
        if (sides.HasFlag(Sides.Right)) margin = MathF.Max(margin, size.Width - point.X + acrossColumns);
        if (sides.HasFlag(Sides.Bottom)) margin = MathF.Max(margin, size.Height - point.Y + acrossRows);
        return float.IsFinite(margin) ? margin : 0;
    }

    // Source pixels one output pixel crosses toward a side between columns, and toward a side between rows.
    private static (float Columns, float Rows) Across(Matrix3x2 toSource)
        => (new Vector2(toSource.M11, toSource.M21).Length(), new Vector2(toSource.M12, toSource.M22).Length());

    // The linear part of a triangle's map from one set of its corners to the other, for row vectors.
    private static bool TryMap(SKPoint[] from, SKPoint[] to, int a, int b, int c, out Matrix3x2 map)
    {
        map = default;
        if (!Matrix3x2.Invert(Basis(from), out Matrix3x2 inverse)) return false;

        map = inverse * Basis(to);
        return float.IsFinite(map.M11) && float.IsFinite(map.M12)
               && float.IsFinite(map.M21) && float.IsFinite(map.M22);

        Matrix3x2 Basis(SKPoint[] points)
            => new(points[b].X - points[a].X, points[b].Y - points[a].Y,
                points[c].X - points[a].X, points[c].Y - points[a].Y, 0, 0);
    }

    private static Vector2 ToVector(SKPoint point) => new(point.X, point.Y);

    // Subset to the semantic source footprint when the backing buffer includes an apron so Clamp never bleeds apron pixels.
    private static SKShader? CreateSourceShader(
        SKImage? image,
        GRRecordingContext? recordingContext,
        Rect sourceBounds,
        RenderPlacement src,
        SKSamplingOptions sampling,
        out Rect rasterBounds,
        out SKSizeI size)
    {
        rasterBounds = src.RasterBounds;
        size = default;
        if (image is null) return null;

        var imageBounds = new PixelRect(new PixelSize(image.Width, image.Height));
        (_, PixelRect semanticSubset, Rect semanticRasterBounds) = RasterShaderMapping.ResolveSemanticSubset(
            sourceBounds, src.Scale.Value, src.DeviceBounds, src.RasterBounds);

        if (semanticSubset != imageBounds
            && semanticSubset.Width > 0 && semanticSubset.Height > 0
            && imageBounds.Contains(semanticSubset))
        {
            using SKImage? subset = RasterShaderMapping.SubsetImage(image, recordingContext, semanticSubset);
            if (subset is not null)
            {
                rasterBounds = semanticRasterBounds;
                size = new SKSizeI(subset.Width, subset.Height);
                return subset.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            }
        }

        size = new SKSizeI(image.Width, image.Height);
        return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
    }

    private struct Corner
    {
        public Vector2 Normals;
        public int Edges;
        public Sides Sides;
        public Matrix3x2 ToOutput;
        public int Maps;
    }

    // The mesh with its outline moved out: its vertices followed by the moved corners, its triangles in draw order with
    // the strips among them, the sides of the source the outline runs along, and how far inside those sides, in source
    // pixels, a fade can reach.
    private sealed record Extension(SKPoint[] Texs, SKPoint[] Positions, int[] Indices, Sides Fading, float Reach);

    // Vertices and the triangles to draw, in order. The alpha of a vertex's color is how much of a pixel there the
    // source covers.
    private sealed record Mesh(SKPoint[] Texs, SKPoint[] Positions, SKColor[]? Colors, int[] Indices)
    {
        public void Draw(SKCanvas canvas, SKPaint paint)
        {
            if (Positions.Length <= ushort.MaxValue + 1)
            {
                using SKVertices all = SKVertices.CreateCopy(SKVertexMode.Triangles, Positions, Texs, Colors,
                    Array.ConvertAll(Indices, static index => (ushort)index));
                canvas.DrawVertices(all, SKBlendMode.Modulate, paint);
                return;
            }

            // Draws the triangles in order in parts whose vertices fit 16-bit indices.
            var local = new Dictionary<int, ushort>();
            var positions = new List<SKPoint>();
            var texs = new List<SKPoint>();
            List<SKColor>? colors = Colors is null ? null : [];
            var indices = new List<ushort>();
            for (int i = 0; i + 2 < Indices.Length; i += 3)
            {
                if (positions.Count + 3 > ushort.MaxValue + 1)
                    Flush();

                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = Indices[i + corner];
                    if (!local.TryGetValue(vertex, out ushort index))
                    {
                        index = (ushort)positions.Count;
                        local.Add(vertex, index);
                        positions.Add(Positions[vertex]);
                        texs.Add(Texs[vertex]);
                        colors?.Add(Colors![vertex]);
                    }

                    indices.Add(index);
                }
            }

            Flush();

            void Flush()
            {
                if (indices.Count > 0)
                {
                    using SKVertices part = SKVertices.CreateCopy(SKVertexMode.Triangles, positions.ToArray(),
                        texs.ToArray(), colors?.ToArray(), indices.ToArray());
                    canvas.DrawVertices(part, SKBlendMode.Modulate, paint);
                }

                local.Clear();
                positions.Clear();
                texs.Clear();
                colors?.Clear();
                indices.Clear();
            }
        }
    }

    // Puts the coverage of the sides that fade into the colors of each triangle's own vertices, so that source-over
    // still shows whatever a triangle lies over where it fades. A color only changes linearly across a triangle, so a
    // triangle is cut first where its coverage starts and stops changing. Each vertex has one distance to each side,
    // and an edge two triangles share is cut once, so both keep the same vertices along it.
    private sealed class Fader
    {
        private readonly Extension _mesh;
        private readonly List<SKPoint> _texs;
        private readonly List<SKPoint> _positions;
        // How far a vertex lies outside the left, top, right and bottom side, in output pixels.
        private readonly List<Vector4> _outside;
        private readonly List<SKColor> _colors;
        private readonly List<int> _indices = [];
        private readonly Dictionary<(int, int), int[]> _cuts = [];

        public Fader(Extension mesh, SKSizeI size)
        {
            _mesh = mesh;
            int count = mesh.Texs.Length;
            _texs = new List<SKPoint>(mesh.Texs);
            _positions = new List<SKPoint>(mesh.Positions);
            _outside = new List<Vector4>(count);
            _colors = new List<SKColor>(count);

            // Each vertex near a side that fades measures the distance to it at the average scale of its triangles.
            Vector3[] scales = ArrayPool<Vector3>.Shared.Rent(count);
            try
            {
                Array.Clear(scales, 0, count);
                int[] indices = mesh.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];
                    if (!Reaches(a) && !Reaches(b) && !Reaches(c)) continue;
                    if (!TryMap(mesh.Positions, mesh.Texs, a, b, c, out Matrix3x2 toSource)) continue;

                    (float columns, float rows) = Across(toSource);
                    if (!(columns > 0) || !(rows > 0) || !float.IsFinite(columns) || !float.IsFinite(rows)) continue;

                    var scale = new Vector3(columns, rows, 1);
                    scales[a] += scale;
                    scales[b] += scale;
                    scales[c] += scale;
                }

                for (int vertex = 0; vertex < count; vertex++)
                {
                    var outside = new Vector4(FarInside);
                    Vector3 scale = scales[vertex];
                    if (scale.Z > 0)
                    {
                        float columns = scale.X / scale.Z;
                        float rows = scale.Y / scale.Z;
                        SKPoint tex = mesh.Texs[vertex];
                        if (mesh.Fading.HasFlag(Sides.Left)) outside.X = -tex.X / columns;
                        if (mesh.Fading.HasFlag(Sides.Top)) outside.Y = -tex.Y / rows;
                        if (mesh.Fading.HasFlag(Sides.Right)) outside.Z = (tex.X - size.Width) / columns;
                        if (mesh.Fading.HasFlag(Sides.Bottom)) outside.W = (tex.Y - size.Height) / rows;
                    }

                    _outside.Add(outside);
                    _colors.Add(ColorOf(outside));
                }
            }
            finally
            {
                ArrayPool<Vector3>.Shared.Return(scales);
            }

            bool Reaches(int vertex)
            {
                SKPoint tex = mesh.Texs[vertex];
                float reach = mesh.Reach;
                return (mesh.Fading.HasFlag(Sides.Left) && tex.X < reach)
                       || (mesh.Fading.HasFlag(Sides.Top) && tex.Y < reach)
                       || (mesh.Fading.HasFlag(Sides.Right) && tex.X > size.Width - reach)
                       || (mesh.Fading.HasFlag(Sides.Bottom) && tex.Y > size.Height - reach);
            }
        }

        public Mesh Build()
        {
            int[] indices = _mesh.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
                Add(indices[i], indices[i + 1], indices[i + 2]);

            return new Mesh(_texs.ToArray(), _positions.ToArray(), _colors.ToArray(), _indices.ToArray());
        }

        private void Add(int a, int b, int c)
        {
            // Away from the sides the source covers the whole triangle, which is affine in the distances.
            if (Farthest(_outside[a]) <= -HalfPixel && Farthest(_outside[b]) <= -HalfPixel
                && Farthest(_outside[c]) <= -HalfPixel)
            {
                _indices.Add(a);
                _indices.Add(b);
                _indices.Add(c);
                return;
            }

            List<List<int>> pieces = [[a, .. Cuts(a, b), b, .. Cuts(b, c), c, .. Cuts(c, a)]];
            for (int side = 0; side < 4; side++)
            {
                if (!_mesh.Fading.HasFlag(SideAt(side))) continue;

                foreach (float level in s_fadeEnds)
                    pieces = Cut(pieces, side, level);
            }

            foreach (List<int> piece in pieces)
            {
                if (IsBeyond(piece)) continue;

                for (int k = 1; k + 1 < piece.Count; k++)
                {
                    _indices.Add(piece[0]);
                    _indices.Add(piece[k]);
                    _indices.Add(piece[k + 1]);
                }
            }
        }

        // Where an edge starts and stops fading, from its lower vertex up. Made once for both triangles that share it.
        private int[] Cuts(int a, int b)
        {
            (int low, int high) = a < b ? (a, b) : (b, a);
            if (!_cuts.TryGetValue((low, high), out int[]? points))
            {
                var crossings = new List<(float T, int Side, float Level)>();
                for (int side = 0; side < 4; side++)
                {
                    if (!_mesh.Fading.HasFlag(SideAt(side))) continue;

                    foreach (float level in s_fadeEnds)
                    {
                        float from = _outside[low][side] - level;
                        float to = _outside[high][side] - level;
                        if ((from < 0 && to > 0) || (from > 0 && to < 0))
                            crossings.Add((from / (from - to), side, level));
                    }
                }

                crossings.Sort(static (x, y) => x.T.CompareTo(y.T));
                points = new int[crossings.Count];
                for (int i = 0; i < points.Length; i++)
                    points[i] = Between(low, high, crossings[i].T, crossings[i].Side, crossings[i].Level);
                _cuts.Add((low, high), points);
            }

            if (a == low) return points;

            var reversed = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
                reversed[i] = points[points.Length - 1 - i];
            return reversed;
        }

        // Splits each piece where its distance to one side passes a level. Only the inside of a triangle is crossed
        // here: its edges already have a vertex at every such point.
        private List<List<int>> Cut(List<List<int>> pieces, int side, float level)
        {
            var result = new List<List<int>>(pieces.Count + 1);
            foreach (List<int> piece in pieces)
            {
                var below = new List<int>(piece.Count + 1);
                var above = new List<int>(piece.Count + 1);
                bool hasBelow = false;
                bool hasAbove = false;
                for (int i = 0; i < piece.Count; i++)
                {
                    int p = piece[i];
                    int q = piece[(i + 1) % piece.Count];
                    float fp = _outside[p][side] - level;
                    float fq = _outside[q][side] - level;
                    if (fp <= 0) below.Add(p);
                    if (fp >= 0) above.Add(p);
                    hasBelow |= fp < 0;
                    hasAbove |= fp > 0;
                    if ((fp < 0 && fq > 0) || (fp > 0 && fq < 0))
                    {
                        int point = Between(p, q, fp / (fp - fq), side, level);
                        below.Add(point);
                        above.Add(point);
                    }
                }

                if (hasBelow && below.Count >= 3) result.Add(below);
                if (hasAbove && above.Count >= 3) result.Add(above);
            }

            return result;
        }

        // A piece entirely a half pixel or more outside a side has no coverage.
        private bool IsBeyond(List<int> piece)
        {
            for (int side = 0; side < 4; side++)
            {
                if (!_mesh.Fading.HasFlag(SideAt(side))) continue;

                bool beyond = true;
                foreach (int vertex in piece)
                    beyond &= _outside[vertex][side] >= HalfPixel;
                if (beyond) return true;
            }

            return false;
        }

        private int Between(int p, int q, float t, int side, float level)
        {
            Vector4 outside = Vector4.Lerp(_outside[p], _outside[q], t);
            outside[side] = level;
            _texs.Add(Lerp(_texs[p], _texs[q], t));
            _positions.Add(Lerp(_positions[p], _positions[q], t));
            _outside.Add(outside);
            _colors.Add(ColorOf(outside));
            return _texs.Count - 1;
        }

        private static SKPoint Lerp(SKPoint from, SKPoint to, float t)
            => new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

        // How much of a pixel centered this far outside the sides the source covers, as the alpha of white.
        private static SKColor ColorOf(Vector4 outside)
        {
            Vector4 covered = Vector4.Clamp(new Vector4(HalfPixel) - outside, Vector4.Zero, Vector4.One);
            float coverage = covered.X * covered.Y * covered.Z * covered.W;
            return new SKColor(255, 255, 255, (byte)MathF.Round(coverage * 255));
        }

        private static float Farthest(Vector4 outside)
            => MathF.Max(MathF.Max(outside.X, outside.Y), MathF.Max(outside.Z, outside.W));

        private static Sides SideAt(int side) => (Sides)(1 << side);
    }
}
