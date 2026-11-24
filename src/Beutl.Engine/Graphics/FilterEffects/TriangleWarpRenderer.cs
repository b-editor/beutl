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
    private static readonly Lazy<SKRuntimeEffect> s_coverageEffect = new(CreateCoverageEffect);

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

    /// <summary>An edge of a mesh's outline, and the third corner of the triangle it belongs to.</summary>
    internal readonly record struct OutlineEdge(int Start, int End, int Opposite);

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
        // transparent, and where it runs along a side of the source, the source fades out over one output pixel.
        (Sides fading, float reach) = ExtendOutline(ref texs, ref positions, ref indices, outline, sourceSize);

        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texs, colors: null, indices);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        using ImmediateCanvas canvas = context.Open(output);
        using (canvas.PushDeviceSpace())
        {
            canvas.Clear();
            canvas.Canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
            Fade(canvas.Canvas, texs, positions, indices, sourceSize, fading, reach, columns: true);
            Fade(canvas.Canvas, texs, positions, indices, sourceSize, fading, reach, columns: false);
        }
        return true;

        static SKPoint ToDevice(Point point, Size size, Point origin, float scale)
            => new((float)((origin.X + (double)point.X * size.Width) * scale),
                (float)((origin.Y + (double)point.Y * size.Height) * scale));
    }

    // Moves each corner of the outline out along its miter, as far as the source can still show past it, and joins
    // it to the outline with a strip that continues the map of the triangle beside it. The strip's own aliased edges
    // then fall where the source is transparent, or past a side of the source that fades. Returns those sides and how
    // far inside them, in source pixels, a fade can reach; no sides when the outline cannot move.
    private static (Sides Fading, float Reach) ExtendOutline(
        ref SKPoint[] texs,
        ref SKPoint[] positions,
        ref ushort[] indices,
        ReadOnlySpan<OutlineEdge> outline,
        SKSizeI size)
    {
        if (outline.IsEmpty) return default;

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

        if (moved.Count == 0 || count + moved.Count > ushort.MaxValue + 1)
            return default;

        var strips = new List<ushort>(outline.Length * 6);
        foreach (OutlineEdge edge in outline)
        {
            int start = moved.GetValueOrDefault(edge.Start, edge.Start);
            int end = moved.GetValueOrDefault(edge.End, edge.End);
            if (end != edge.End)
                AddTriangle(edge.Start, edge.End, end);
            if (start != edge.Start)
                AddTriangle(edge.Start, end, start);
        }

        texs = [.. texs, .. movedTexs];
        positions = [.. positions, .. movedPositions];
        indices = [.. indices, .. strips];
        return (fading, reach);

        void AddTriangle(int a, int b, int c)
        {
            strips.Add((ushort)a);
            strips.Add((ushort)b);
            strips.Add((ushort)c);
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

    // Multiplies what the triangles drew by how much of each output pixel the source covers between one pair of
    // opposite sides. Each triangle measures the distance to a side at its own scale, so the side fades over one
    // output pixel however the warp stretches the source along it.
    private static void Fade(
        SKCanvas canvas,
        SKPoint[] texs,
        SKPoint[] positions,
        ushort[] indices,
        SKSizeI size,
        Sides fading,
        float reach,
        bool columns)
    {
        Sides near = fading & (columns ? Sides.Left : Sides.Top);
        Sides far = fading & (columns ? Sides.Right : Sides.Bottom);
        if (near == Sides.None && far == Sides.None) return;

        float extent = columns ? size.Width : size.Height;
        var fadePositions = new List<SKPoint>();
        var distances = new List<SKPoint>();
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];
            // A triangle that keeps to the inside of the reach is left as drawn.
            if (!Reaches(texs[a]) && !Reaches(texs[b]) && !Reaches(texs[c])) continue;
            if (!TryMap(positions, texs, a, b, c, out Matrix3x2 toSource)) continue;

            (float acrossColumns, float acrossRows) = Across(toSource);
            float across = columns ? acrossColumns : acrossRows;
            if (!(across > 0) || !float.IsFinite(across)) continue;

            SKPoint distanceA = Outside(texs[a], across);
            SKPoint distanceB = Outside(texs[b], across);
            SKPoint distanceC = Outside(texs[c], across);
            // A distance is affine over the triangle, so a triangle a half pixel or more inside at its corners is too.
            if (MathF.Max(MathF.Max(distanceA.X, distanceB.X), distanceC.X) <= -0.5f
                && MathF.Max(MathF.Max(distanceA.Y, distanceB.Y), distanceC.Y) <= -0.5f)
                continue;

            fadePositions.Add(positions[a]);
            fadePositions.Add(positions[b]);
            fadePositions.Add(positions[c]);
            distances.Add(distanceA);
            distances.Add(distanceB);
            distances.Add(distanceC);
        }

        if (fadePositions.Count == 0) return;

        using SKVertices fade = SKVertices.CreateCopy(
            SKVertexMode.Triangles, fadePositions.ToArray(), distances.ToArray(), colors: null);
        using SKShader coverage = s_coverageEffect.Value.ToShader();
        using var paint = new SKPaint { Shader = coverage, BlendMode = SKBlendMode.DstIn };
        canvas.DrawVertices(fade, SKBlendMode.Modulate, paint);

        bool Reaches(SKPoint point)
        {
            float position = columns ? point.X : point.Y;
            return (near != Sides.None && position < reach) || (far != Sides.None && position > extent - reach);
        }

        // How far outside the near and the far side a source point lies, in output pixels.
        SKPoint Outside(SKPoint point, float across)
        {
            float position = columns ? point.X : point.Y;
            return new SKPoint(
                near != Sides.None ? -position / across : FarInside,
                far != Sides.None ? (position - extent) / across : FarInside);
        }
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

    private static SKRuntimeEffect CreateCoverageEffect()
        => SKRuntimeEffect.CreateShader("""
            // How much of an output pixel centered this far outside the two sides the source covers.
            half4 main(float2 outside)
            {
                float2 coverage = saturate(0.5 - outside);
                return half4(coverage.x * coverage.y);
            }
            """, out string? error)
            ?? throw new InvalidOperationException($"Could not compile the warp edge coverage shader: {error}");

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
}
