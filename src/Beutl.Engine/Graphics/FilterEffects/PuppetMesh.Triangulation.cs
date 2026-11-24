using System.Numerics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal sealed partial class PuppetMesh
{
    private const int InteriorCellBudget = 2500;
    private const float CellSpacing = 24;

    public static PuppetMesh? Create(IReadOnlyList<Point[]> outlines, Size size, PixelSize maskSize,
        Point[] anchors, out bool exceedsVertexBudget)
    {
        exceedsVertexBudget = false;
        if (outlines.Count == 0) return null;

        List<Edge> edges = GetEdges(outlines, size, maskSize);
        if (edges.Count == 0) return null;

        float left = edges.Min(edge => edge.MinX);
        float right = edges.Max(edge => edge.MaxX);
        float top = edges.Min(edge => edge.Top.Y);
        float bottom = edges.Max(edge => edge.Bottom.Y);
        if (left >= right || top >= bottom) return null;

        double spacing = Math.Max(CellSpacing, Math.Sqrt((double)size.Width * size.Height / InteriorCellBudget));
        int rows = Math.Clamp((int)Math.Ceiling((bottom - (double)top) / spacing), 1, InteriorCellBudget);
        int columns = Math.Clamp((int)Math.Ceiling((right - (double)left) / spacing), 1, InteriorCellBudget / rows);
        float[] gridX = Divide(left, right, columns);
        float[] gridY = Divide(top, bottom, rows);
        var pins = new Vector2?[anchors.Length];
        for (int i = 0; i < pins.Length; i++)
        {
            Point anchor = anchors[i];
            if (anchor.X is >= 0 and <= 1 && anchor.Y is >= 0 and <= 1)
                pins[i] = ToLocal(size, anchor);
        }

        Edge[] scanEdges = edges.Where(edge => edge.Top.Y < edge.Bottom.Y).OrderBy(edge => edge.Top.Y).ToArray();
        var strips = new Strip[columns];
        SortedSet<float>[] seams = Enumerable.Range(0, gridX.Length).Select(static _ => new SortedSet<float>()).ToArray();
        for (int i = 0; i < columns; i++)
            strips[i] = BuildStrip(gridX[i], gridX[i + 1], gridY, edges, scanEdges, pins, seams[i], seams[i + 1]);

        var vertices = new List<Vector2>();
        var indices = new List<ushort>();
        var vertexIndices = new Dictionary<Vector2, int>();
        var upper = new List<float>();
        var lower = new List<float>();
        for (int i = 0; i < strips.Length; i++)
        {
            Strip strip = strips[i];
            foreach (Band band in strip.Bands)
            {
                upper.Clear();
                lower.Clear();
                upper.AddRange(strip.Rows[band.Top].GetViewBetween(band.TopLeft, band.TopRight));
                lower.AddRange(strip.Rows[band.Bottom].GetViewBetween(band.BottomLeft, band.BottomRight));
                SortedSet<float>? leftCuts = band.TopLeft == gridX[i] && band.BottomLeft == gridX[i] ? seams[i] : null;
                SortedSet<float>? rightCuts = band.TopRight == gridX[i + 1] && band.BottomRight == gridX[i + 1]
                    ? seams[i + 1] : null;
                TriangulateBand(band, leftCuts, rightCuts);

                if (vertices.Count > ushort.MaxValue + 1)
                {
                    exceedsVertexBudget = true;
                    return null;
                }
            }
        }

        if (indices.Count == 0) return null;

        var pinVertices = new int[pins.Length];
        for (int i = 0; i < pins.Length; i++)
            pinVertices[i] = pins[i] is { } point && vertexIndices.TryGetValue(point, out int vertex) ? vertex : -1;
        return new PuppetMesh(vertices.ToArray(), indices.ToArray(), pinVertices);

        void TriangulateBand(Band band, SortedSet<float>? leftCuts, SortedSet<float>? rightCuts)
        {
            // Walk both horizontal chains left to right, always taking the shorter available diagonal.
            // Both chains include the vertices of all bands touching that row.
            int u = 0;
            int l = 0;
            while (u + 1 < upper.Count || l + 1 < lower.Count)
            {
                Vector2 topLeft = new(upper[u], band.Top);
                Vector2 bottomLeft = new(lower[l], band.Bottom);
                bool first = u == 0 && l == 0;
                bool stepTop = l + 1 == lower.Count || (u + 1 < upper.Count
                    && Math.Abs(upper[u + 1] - lower[l]) <= Math.Abs(upper[u] - lower[l + 1]));
                Vector2 next = stepTop ? new Vector2(upper[++u], band.Top) : new Vector2(lower[++l], band.Bottom);
                bool last = u + 1 == upper.Count && l + 1 == lower.Count;
                if (first && leftCuts is not null)
                {
                    AddSeamTriangles(leftCuts, next, band.TopLeft, band.Top, band.Bottom, isLeft: true);
                }
                else if (last && rightCuts is not null)
                {
                    Vector2 tip = stepTop ? topLeft : bottomLeft;
                    AddSeamTriangles(rightCuts, tip, band.TopRight, band.Top, band.Bottom, isLeft: false);
                }
                else
                {
                    AddTriangle(topLeft, next, bottomLeft);
                }
            }
        }

        void AddSeamTriangles(SortedSet<float> cuts, Vector2 tip, float x, float top, float bottom, bool isLeft)
        {
            // Split a column edge at every boundary point from either side of the seam.
            Vector2 previous = new(x, top);
            foreach (float y in cuts.GetViewBetween(top, bottom))
            {
                if (y <= top) continue;

                var point = new Vector2(x, y);
                AddTriangle(tip, isLeft ? point : previous, isLeft ? previous : point);
                previous = point;
            }
        }

        void AddTriangle(Vector2 a, Vector2 b, Vector2 c)
        {
            if (Cross(b - a, c - a) <= 0) return;

            indices.Add((ushort)AddVertex(a));
            indices.Add((ushort)AddVertex(b));
            indices.Add((ushort)AddVertex(c));
        }

        int AddVertex(Vector2 point)
        {
            if (vertexIndices.TryGetValue(point, out int index)) return index;

            index = vertices.Count;
            vertices.Add(point);
            vertexIndices.Add(point, index);
            return index;
        }
    }

    private static Strip BuildStrip(float left, float right, float[] gridY, List<Edge> edges, Edge[] scanEdges,
        Vector2?[] pins, SortedSet<float> leftSeam, SortedSet<float> rightSeam)
    {
        // Only contour events within this strip cut its bands. The regular grid keeps the interior deformable
        // without propagating every pixel-sized boundary change across the full width of the image.
        var events = new SortedSet<float>(gridY);
        foreach (Edge edge in edges)
            edge.AddEvents(left, right, events);

        var strip = new Strip();
        foreach (Vector2? value in pins)
        {
            if (value is { } pin && pin.X >= left && pin.X <= right && pin.Y >= gridY[0] && pin.Y <= gridY[^1])
            {
                events.Add(pin.Y);
                Row(pin.Y).Add(pin.X);
            }
        }

        float[] levels = events.ToArray();
        var active = new List<int>();
        var hits = new List<(double X, int Edge)>();
        int next = 0;
        for (int i = 1; i < levels.Length; i++)
        {
            float top = levels[i - 1];
            float bottom = levels[i];
            double middle = ((double)top + bottom) * 0.5;
            // Other strips can have a contour vertex at this exact height. Count each edge on
            // [Top.Y, Bottom.Y) so a shared endpoint cannot make a boundary crossing disappear.
            while (next < scanEdges.Length && scanEdges[next].Top.Y <= middle)
                active.Add(next++);

            hits.Clear();
            for (int k = active.Count - 1; k >= 0; k--)
            {
                int index = active[k];
                Edge edge = scanEdges[index];
                if (edge.Bottom.Y <= middle)
                {
                    active[k] = active[^1];
                    active.RemoveAt(active.Count - 1);
                }
                else
                {
                    hits.Add((edge.XAt(middle), index));
                }
            }
            hits.Sort();

            // Even-odd fill preserves holes, including islands nested inside holes.
            for (int k = 1; k < hits.Count; k += 2)
            {
                (double from, int leftEdge) = hits[k - 1];
                (double to, int rightEdge) = hits[k];
                if (Math.Max(left, from) >= Math.Min(right, to)) continue;

                Edge a = scanEdges[leftEdge];
                Edge b = scanEdges[rightEdge];
                // A contour outside the strip can change edges between these rows. Its extrapolation
                // is irrelevant: the filled interval stays clipped to the seam until it crosses it.
                float topLeft = from <= left ? left : a.ClipX(top, left, right);
                float bottomLeft = from <= left ? left : a.ClipX(bottom, left, right);
                float topRight = to >= right ? right : Math.Max(topLeft, b.ClipX(top, left, right));
                float bottomRight = to >= right ? right : Math.Max(bottomLeft, b.ClipX(bottom, left, right));
                if (topLeft == topRight && bottomLeft == bottomRight) continue;

                strip.Bands.Add(new Band(top, bottom, topLeft, topRight, bottomLeft, bottomRight));
                Record(top, topLeft, topRight);
                Record(bottom, bottomLeft, bottomRight);
            }
        }
        return strip;

        SortedSet<float> Row(float y)
        {
            if (!strip.Rows.TryGetValue(y, out SortedSet<float>? cuts))
                strip.Rows.Add(y, cuts = []);
            return cuts;
        }

        void Record(float y, float start, float end)
        {
            SortedSet<float> cuts = Row(y);
            cuts.Add(start);
            cuts.Add(end);
            if (start == left || end == left) leftSeam.Add(y);
            if (start == right || end == right) rightSeam.Add(y);
        }
    }

    private static List<Edge> GetEdges(IReadOnlyList<Point[]> outlines, Size size, PixelSize maskSize)
    {
        using var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        foreach (Point[] outline in outlines)
        {
            if (outline.Length >= 3)
                ContourPaths.AddClosedPolyline(builder, outline, default);
        }

        using SKPath path = builder.Detach();
        // Resolve coincident edges and self-touches before sweeping the line segments.
        using SKPath? simplified = path.Simplify();
        using SKPath.RawIterator iterator = (simplified ?? path).CreateRawIterator();
        Span<SKPoint> points = stackalloc SKPoint[4];
        var edges = new List<Edge>();
        float sx = size.Width / maskSize.Width;
        float sy = size.Height / maskSize.Height;
        Vector2 start = default;
        Vector2 previous = default;
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            if (verb == SKPathVerb.Move)
            {
                start = previous = ToPoint(points[0]);
            }
            else if (verb is SKPathVerb.Line or SKPathVerb.Close)
            {
                Vector2 current = verb == SKPathVerb.Close ? start : ToPoint(points[1]);
                if (previous != current)
                    edges.Add(previous.Y <= current.Y ? new Edge(previous, current) : new Edge(current, previous));
                previous = current;
            }
        }
        return edges;

        Vector2 ToPoint(SKPoint point) => new(
            Math.Clamp(point.X * sx, 0, size.Width),
            Math.Clamp(point.Y * sy, 0, size.Height));
    }

    private static float[] Divide(float start, float end, int count)
    {
        var values = new float[count + 1];
        for (int i = 0; i < count; i++)
            values[i] = (float)(start + (end - (double)start) * i / count);
        values[^1] = end;
        return values;
    }

    private sealed class Strip
    {
        public List<Band> Bands { get; } = [];
        public Dictionary<float, SortedSet<float>> Rows { get; } = [];
    }

    private readonly record struct Band(float Top, float Bottom, float TopLeft, float TopRight,
        float BottomLeft, float BottomRight);

    private readonly record struct Edge(Vector2 Top, Vector2 Bottom)
    {
        public float MinX => Math.Min(Top.X, Bottom.X);
        public float MaxX => Math.Max(Top.X, Bottom.X);

        public double XAt(double y) => Top.X + (y - Top.Y) * (Bottom.X - (double)Top.X) / (Bottom.Y - (double)Top.Y);

        private float YAt(float x) => (float)(Top.Y + (x - (double)Top.X) * (Bottom.Y - (double)Top.Y) / (Bottom.X - (double)Top.X));

        private bool Crosses(float x) => MinX < x && x < MaxX;

        public float ClipX(float y, float left, float right)
        {
            // Use the exact seam coordinate at a recorded crossing. Interpolating X from rounded Y could
            // otherwise put the two neighboring strips a fraction of a pixel apart at the same crossing.
            if (Crosses(left) && YAt(left) == y) return left;
            if (Crosses(right) && YAt(right) == y) return right;
            return (float)Math.Clamp(XAt(y), left, right);
        }

        public void AddEvents(float left, float right, SortedSet<float> events)
        {
            if (Top.X >= left && Top.X <= right) events.Add(Top.Y);
            if (Bottom.X >= left && Bottom.X <= right) events.Add(Bottom.Y);
            if (Crosses(left)) events.Add(YAt(left));
            if (Crosses(right)) events.Add(YAt(right));
        }
    }
}
