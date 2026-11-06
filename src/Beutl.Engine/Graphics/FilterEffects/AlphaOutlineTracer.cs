using System.Buffers;
using System.Numerics;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Traces the outlines of the content in a bitmap's alpha at sub-pixel precision.
/// </summary>
/// <remarks>
/// <para>
/// An outline runs where the edge pixels are half covered. Marching squares over the alpha places each crossing by
/// linear interpolation between pixel centres, so a curve or a slanted side comes out as a smooth line rather than in
/// whole-pixel steps. An edge pixel's alpha is measured against the highest alpha next to it, so a semi-transparent
/// shape is outlined at its own edge, and a pixel that does not touch the background is inside whatever its alpha,
/// so a shape is never cut along its own semi-transparent parts. Alpha below about 3 % is background, and an island
/// or hole smaller than a square pixel is left out.
/// </para>
/// <para>
/// Half coverage also cuts a sharp corner back, which a mitered pen would draw as a bevel, so where two straight
/// sides meet across such a cut, the corner is put back where they meet. Each outline is then simplified to within a
/// tenth of a pixel.
/// </para>
/// <para>
/// This is the outline to draw: to stroke, fill or extrude the content's visible edge, as <see cref="StrokeEffect"/>
/// and <see cref="FlatShadow"/> do. <see cref="ContourTracer"/> instead follows the pixels whose alpha is above zero
/// in whole-pixel steps, with the hierarchy of holes, for telling which pixels belong to which part.
/// </para>
/// </remarks>
public static class AlphaOutlineTracer
{
    // An alpha below this is background: about 3 %, so the faintest fringe of a blur or a stray dithered pixel does
    // not outline anything. A pixel is never measured against less than twice this, which keeps an 8-bit fringe
    // from being stretched into noise.
    internal const int BackgroundAlpha = 8;

    // The crossing level on the 0-255 scale of the normalized alpha.
    private const float Level = 127.5f;

    // How far the simplified outline may stray from the traced one, in pixels.
    private const float Tolerance = 0.1f;

    // Corners are found on a coarser simplification, which keeps an anti-aliased side one segment even where the
    // crossings wobble: interpolation puts them up to a tenth of a pixel either side of a side drawn with smooth
    // coverage, and further where the source was drawn with four coverage samples.
    private const float CornerTolerance = 0.25f;

    // A corner is where two sides at least this long meet, turning by between the two angles. A curve gentle enough
    // to simplify into sides that long, with a radius of eight pixels or more, turns less than the smaller angle
    // between them.
    private const float CornerSideLength = 4f;
    private static readonly float s_cornerMinTurnCos = MathF.Cos(40f * MathF.PI / 180f);
    private static readonly float s_cornerMaxTurnCos = MathF.Cos(165f * MathF.PI / 180f);

    // Half coverage cuts a sharp corner back to where it is narrower than about half a pixel: some 0.3 px at a
    // right angle and further as it sharpens, in proportion to one over the tangent of half the inside angle, more
    // again with four coverage samples. A corner is put back when the outline comes within this cut over that
    // tangent of where the sides meet, which a corner rounded with a radius over about two and a half pixels at a
    // right angle, or one and a half at a star's point, does not; and when everything the outline does between the
    // sides keeps to one of them or stays within the reach of a corner of this radius.
    private const float CornerCut = 1f;
    private const float CornerRadius = 1.5f;
    private const float CornerMaxReach = 8f;
    private const float CornerOvershoot = 0.25f;

    // How far a point may stray from a side's fitted line and still keep to it: the crossings' wobble after
    // averaging, with four-sample coverage, and the fit's own error. The cut above is what tells a rounded corner.
    private const float CornerSideTolerance = 0.4f;

    // An island or hole smaller than this, in square pixels, is a single stray pixel: outlining it would draw a
    // ring the size of the pen.
    private const float MinArea = 1f;

    private const int Top = 0;
    private const int Right = 1;
    private const int Bottom = 2;
    private const int Left = 3;

    // The sides a contour enters a cell through, by case: bit 8 is the top-left corner inside, 4 top-right,
    // 2 bottom-right and 1 bottom-left. Two entries means two pieces.
    private static readonly int[][] s_entries =
    [
        [], [Left], [Bottom], [Left], [Right], [Left, Right], [Bottom], [Left],
        [Top], [Top], [Top, Bottom], [Top], [Right], [Right], [Bottom], [],
    ];

    /// <summary>Traces the outlines of the content in a bitmap's alpha.</summary>
    /// <param name="bitmap">
    /// The bitmap to trace. A color type other than <see cref="BitmapColorType.Alpha8"/> is converted to it first.
    /// </param>
    /// <returns>
    /// One closed polygon per outline, its last point joining its first, in the bitmap's pixel space, where (0, 0) is
    /// the top-left corner of the first pixel. Outer outlines run clockwise on screen and holes counter-clockwise, so
    /// filling them with the non-zero rule fills the content. Empty when the bitmap has no content.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="bitmap"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<Point[]> Trace(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.ColorType == BitmapColorType.Alpha8)
            return TraceAlpha8(bitmap);

        using Bitmap alpha = bitmap.Convert(BitmapColorType.Alpha8);
        return TraceAlpha8(alpha);
    }

    private static List<Point[]> TraceAlpha8(Bitmap bitmap)
    {
        var result = new List<Point[]>();
        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0)
            return result;

        // One sample per pixel centre, with a ring of background around the bitmap so every contour closes.
        int columns = width + 2;
        int rows = height + 2;
        int horizontalEdges = (columns - 1) * rows;
        int edgeCount = horizontalEdges + columns * (rows - 1);
        int visitedWords = (edgeCount + 63) / 64;
        byte[] levels = ArrayPool<byte>.Shared.Rent(columns * rows);
        ulong[] visited = ArrayPool<ulong>.Shared.Rent(visitedWords);
        try
        {
            FillLevels(bitmap, levels, columns, rows);
            Array.Clear(visited, 0, visitedWords);
            var field = new Field(levels, columns, horizontalEdges, visited);
            var points = new List<Vector2>();
            for (int j = 0; j < rows - 1; j++)
            {
                ReadOnlySpan<byte> top = levels.AsSpan(j * columns, columns);
                ReadOnlySpan<byte> bottom = levels.AsSpan((j + 1) * columns, columns);
                int i = 0;
                while (i < columns - 1)
                {
                    // Skip the cells up to the first sample, in either row, that leaves a run of background or of
                    // solid content.
                    byte value = top[i];
                    if (value is 0 or 255 && bottom[i] == value)
                    {
                        int next = Math.Min(Differs(top, i + 1, value), Differs(bottom, i + 1, value)) - 1;
                        if (next > i)
                        {
                            i = next;
                            continue;
                        }
                    }

                    int shape = field.Case(i, j);
                    if (shape is not (0 or 15))
                    {
                        foreach (int entry in s_entries[shape])
                        {
                            if (field.IsVisited(field.Edge(i, j, entry)))
                                continue;

                            points.Clear();
                            field.Follow(i, j, entry, points);
                            if (Finish(points) is { } polygon)
                                result.Add(polygon);
                        }
                    }

                    i++;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(levels);
            ArrayPool<ulong>.Shared.Return(visited);
        }

        return result;
    }

    // The index of the first sample from start on that is not the value, or the row's length.
    private static int Differs(ReadOnlySpan<byte> samples, int start, byte value)
    {
        int index = samples.Slice(start).IndexOfAnyExcept(value);
        return index < 0 ? samples.Length : start + index;
    }

    // Fills the samples with the alpha normalized against its neighbourhood, 0 to 255: the background ring and
    // transparent pixels are 0, opaque pixels and pixels clear of the background are 255.
    private static void FillLevels(Bitmap bitmap, byte[] levels, int columns, int rows)
    {
        int width = columns - 2;
        int height = rows - 2;
        levels.AsSpan(0, columns).Clear();
        levels.AsSpan((rows - 1) * columns, columns).Clear();
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> above = y > 0 ? bitmap.GetRow(y - 1) : default;
            ReadOnlySpan<byte> row = bitmap.GetRow(y);
            ReadOnlySpan<byte> below = y < height - 1 ? bitmap.GetRow(y + 1) : default;
            Span<byte> samples = levels.AsSpan((y + 1) * columns, columns);
            samples[0] = 0;
            samples[columns - 1] = 0;
            row.CopyTo(samples.Slice(1));
            // Only a partly covered pixel needs its neighbourhood.
            int x = 0;
            while (row.Slice(x).IndexOfAnyExcept((byte)0, (byte)255) is >= 0 and var offset)
            {
                x += offset;
                samples[x + 1] = EdgeLevel(row[x], x, width, above, row, below);
                x++;
            }
        }
    }

    private static byte EdgeLevel(
        int alpha, int x, int width, ReadOnlySpan<byte> above, ReadOnlySpan<byte> row, ReadOnlySpan<byte> below)
    {
        int min = 255;
        int max = 0;
        // Whatever lies outside the bitmap is background.
        if (above.IsEmpty || below.IsEmpty || x == 0 || x == width - 1)
            min = 0;

        int from = Math.Max(x - 1, 0);
        int to = Math.Min(x + 1, width - 1);
        for (int k = from; k <= to; k++)
        {
            if (!above.IsEmpty)
                Include(above[k], ref min, ref max);
            Include(row[k], ref min, ref max);
            if (!below.IsEmpty)
                Include(below[k], ref min, ref max);
        }

        if (alpha >= BackgroundAlpha && min >= BackgroundAlpha)
            return 255;

        return (byte)MathF.Round(alpha * 255f / Math.Max(max, 2 * BackgroundAlpha));

        static void Include(int value, ref int min, ref int max)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }
    }

    private static Point[]? Finish(List<Vector2> points)
    {
        if (points.Count < 3 || MathF.Abs(SignedArea(points)) < MinArea)
            return null;

        Smooth(points);
        List<int> anchors = RestoreCorners(points);
        List<int> kept = Simplify(points, Tolerance, anchors);
        if (kept.Count < 3)
            return null;

        var polygon = new Point[kept.Count];
        for (int k = 0; k < kept.Count; k++)
            polygon[k] = new Point(points[kept[k]].X, points[kept[k]].Y);

        return polygon;
    }

    // Twice the area is positive for a contour that runs clockwise on screen.
    private static float SignedArea(List<Vector2> points)
    {
        float sum = 0;
        Vector2 previous = points[^1];
        foreach (Vector2 point in points)
        {
            sum += previous.X * point.Y - point.X * previous.Y;
            previous = point;
        }

        return sum / 2;
    }

    // Averages each point with its neighbours, weighted 1, 2, 1: cancels the alternating error of crossings on
    // the two grid directions while moving a curve by a few hundredths of a pixel.
    private static void Smooth(List<Vector2> points)
    {
        int count = points.Count;
        Vector2 first = points[0];
        Vector2 previous = points[count - 1];
        for (int k = 0; k < count; k++)
        {
            Vector2 current = points[k];
            Vector2 next = k + 1 < count ? points[k + 1] : first;
            points[k] = (previous + 2 * current + next) / 4;
            previous = current;
        }
    }

    // Douglas-Peucker on a closed contour, returning the indices it keeps in order. It runs between the anchors,
    // or else between the leftmost point and the point farthest from it, which any simplification keeps.
    private static List<int> Simplify(List<Vector2> points, float tolerance, List<int>? anchors)
    {
        int count = points.Count;
        List<int> splits;
        if (anchors is { Count: > 0 })
        {
            splits = anchors;
        }
        else
        {
            int first = 0;
            for (int k = 1; k < count; k++)
            {
                if (points[k].X < points[first].X
                    || (points[k].X == points[first].X && points[k].Y < points[first].Y))
                {
                    first = k;
                }
            }

            int second = first;
            float farthest = -1;
            for (int k = 0; k < count; k++)
            {
                float distance = Vector2.DistanceSquared(points[k], points[first]);
                if (distance > farthest)
                {
                    farthest = distance;
                    second = k;
                }
            }

            splits = first == second ? [first] : [Math.Min(first, second), Math.Max(first, second)];
        }

        var keep = new bool[count];
        var pending = new Stack<(int Start, int End)>();
        for (int k = 0; k < splits.Count; k++)
        {
            keep[splits[k]] = true;
            // Indices past the end wrap around, so each stretch is an increasing range.
            int end = k + 1 < splits.Count ? splits[k + 1] : splits[0] + count;
            pending.Push((splits[k], end));
        }

        float limit = tolerance * tolerance;
        while (pending.Count > 0)
        {
            (int start, int end) = pending.Pop();
            if (end - start < 2)
                continue;

            Vector2 a = points[start % count];
            Vector2 b = points[end % count];
            float worst = limit;
            int split = -1;
            for (int k = start + 1; k < end; k++)
            {
                float distance = SegmentDistanceSquared(points[k % count], a, b);
                if (distance > worst)
                {
                    worst = distance;
                    split = k;
                }
            }

            if (split < 0)
                continue;

            keep[split % count] = true;
            pending.Push((start, split));
            pending.Push((split, end));
        }

        var result = new List<int>();
        for (int k = 0; k < count; k++)
        {
            int index = (splits[0] + k) % count;
            if (keep[index])
                result.Add(index);
        }

        return result;
    }

    private static float SegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared == 0)
            return Vector2.DistanceSquared(point, a);

        float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0, 1);
        return Vector2.DistanceSquared(point, a + t * ab);
    }

    // Moves each corner half coverage cut back to where its sides meet, removing the points around it, and returns
    // the corners' indices in order for the simplification to keep.
    private static List<int> RestoreCorners(List<Vector2> points)
    {
        int count = points.Count;
        var anchors = new List<int>();
        List<int> coarse = Simplify(points, CornerTolerance, null);
        int sides = coarse.Count;
        if (sides < 3)
            return anchors;

        bool IsSide(int k) =>
            Vector2.Distance(points[coarse[k % sides]], points[coarse[(k + 1) % sides]]) >= CornerSideLength;

        int start = -1;
        for (int k = 0; k < sides; k++)
        {
            if (IsSide(k))
            {
                start = k;
                break;
            }
        }

        if (start < 0)
            return anchors;

        // Each pair of consecutive sides, with whatever short segments lie between them, may meet at a corner.
        var corners = new List<(int First, int Last, Vector2 Point)>();
        var claimed = new bool[count];
        int inSide = start;
        do
        {
            int outSide = inSide + 1;
            while (!IsSide(outSide))
                outSide++;

            if (TryCorner(points, coarse, inSide, outSide, claimed) is { } corner)
                corners.Add(corner);

            inSide = outSide;
        } while (inSide < start + sides);

        if (corners.Count == 0)
            return anchors;

        // Each corner replaces the points from its first to its last, which may wrap past the end.
        var replacement = new Vector2?[count];
        foreach ((int first, int last, Vector2 point) in corners)
            replacement[first] = point;

        var result = new List<Vector2>(count);
        for (int k = 0; k < count; k++)
        {
            if (replacement[k] is { } point)
            {
                anchors.Add(result.Count);
                result.Add(point);
            }
            else if (!claimed[k])
            {
                result.Add(points[k]);
            }
        }

        points.Clear();
        points.AddRange(result);
        return anchors;
    }

    // The corner between two sides of the coarse outline, given as its segment indices, which may run past its end.
    private static (int First, int Last, Vector2 Point)? TryCorner(
        List<Vector2> points, List<int> coarse, int inSide, int outSide, bool[] claimed)
    {
        int count = points.Count;
        int sides = coarse.Count;
        int inStart = coarse[inSide % sides];
        int inEnd = coarse[(inSide + 1) % sides];
        int outStart = coarse[outSide % sides];
        int outEnd = coarse[(outSide + 1) % sides];
        (Vector2 inPoint, Vector2 inDirection) = FitSide(points, inStart, inEnd, cornerAtEnd: true);
        (Vector2 outPoint, Vector2 outDirection) = FitSide(points, outStart, outEnd, cornerAtEnd: false);
        float turnCos = Vector2.Dot(inDirection, outDirection);
        if (turnCos > s_cornerMinTurnCos || turnCos < s_cornerMaxTurnCos)
            return null;

        float sine = Cross(inDirection, outDirection);
        Vector2 corner = inPoint + Cross(outPoint - inPoint, outDirection) / sine * inDirection;
        // The sides must meet ahead of where the in-side ends and behind where the out-side starts.
        if (Vector2.Dot(corner - points[inEnd], inDirection) < -CornerOvershoot
            || Vector2.Dot(points[outStart] - corner, outDirection) < -CornerOvershoot)
        {
            return null;
        }

        // Half the inside angle is half of 180 degrees less the turn; its tangent is (1 + cos turn) / sin turn.
        float halfAngleTan = (1 + turnCos) / MathF.Abs(sine);
        float reach = MathF.Min(CornerRadius / halfAngleTan, CornerMaxReach);

        // The corner takes every point from the in-side's end to the out-side's start, and as far along each side
        // as stays within reach, without taking either side whole. Each of them must keep to a side or stay within
        // reach.
        int Wrap(int index) => (index % count + count) % count;
        int span = Wrap(outStart - inEnd);
        int first = 0;
        int inLength = Wrap(inEnd - inStart);
        while (first > -inLength + 1 && Vector2.Distance(points[Wrap(inEnd + first - 1)], corner) <= reach)
            first--;

        int last = span;
        int outLength = Wrap(outEnd - outStart);
        while (last < span + outLength - 1 && Vector2.Distance(points[Wrap(inEnd + last + 1)], corner) <= reach)
            last++;

        for (int k = first; k <= last; k++)
        {
            int index = Wrap(inEnd + k);
            Vector2 offset = points[index] - corner;
            bool fits = offset.Length() <= reach
                        || (MathF.Abs(Cross(offset, inDirection)) <= CornerSideTolerance
                            && Vector2.Dot(offset, inDirection) <= CornerOvershoot)
                        || (MathF.Abs(Cross(offset, outDirection)) <= CornerSideTolerance
                            && Vector2.Dot(offset, outDirection) >= -CornerOvershoot);
            if (claimed[index] || !fits)
                return null;
        }

        // A rounded corner keeps its distance from where its sides meet.
        float closest = Vector2.Distance(points[Wrap(inEnd + first)], corner);
        for (int k = first; k < last; k++)
        {
            float distance = MathF.Sqrt(
                SegmentDistanceSquared(corner, points[Wrap(inEnd + k)], points[Wrap(inEnd + k + 1)]));
            closest = MathF.Min(closest, distance);
        }

        if (closest > CornerCut / halfAngleTan)
            return null;

        for (int k = first; k <= last; k++)
            claimed[Wrap(inEnd + k)] = true;

        return (Wrap(inEnd + first), Wrap(inEnd + last), corner);
    }

    // The line of a side, fitted to the traced points along it. Its ends may already bend into the corners, so only
    // its middle counts, unless the outline runs straight on past the far end, where the coarse outline split one
    // side in two: then the side is fitted whole from a fifth of the way in from the corner, since a sharp corner's
    // position rests on its sides' directions, the more so the sharper it is. Returns a point on the line and its
    // direction from start to end.
    private static (Vector2 Point, Vector2 Direction) FitSide(List<Vector2> points, int start, int end, bool cornerAtEnd)
    {
        int count = points.Count;
        int length = ((end - start) % count + count) % count;
        Vector2 from = points[start];
        Vector2 chord = points[end] - from;
        float lengthSquared = chord.LengthSquared();
        Vector2 chordDirection = Vector2.Normalize(chord);
        float Along(Vector2 point) => Vector2.Dot(point - from, chord) / lengthSquared;

        var middle = new LineFit();
        for (int k = 0; k <= length; k++)
        {
            Vector2 point = points[(start + k) % count];
            if (Along(point) is >= 0.2f and <= 0.8f)
                middle.Add(point);
        }

        if (middle.Count < 2)
            return (from, chordDirection);

        (Vector2 centre, Vector2 direction) = middle.Line(chordDirection);
        bool OnLine(Vector2 point) => MathF.Abs(Cross(point - centre, direction)) <= CornerTolerance;

        LineFit whole = middle;
        for (int k = 0; k <= length; k++)
        {
            Vector2 point = points[(start + k) % count];
            float t = Along(point);
            if ((cornerAtEnd ? t < 0.2f : t > 0.8f) && OnLine(point))
                whole.Add(point);
        }

        int far = cornerAtEnd ? start : end;
        int step = cornerAtEnd ? -1 : 1;
        Vector2 previous = points[far];
        float run = 0;
        for (int k = 1; k < count - length; k++)
        {
            Vector2 point = points[((far + step * k) % count + count) % count];
            if (!OnLine(point))
                break;

            run += Vector2.Distance(point, previous);
            previous = point;
            whole.Add(point);
        }

        return run >= CornerSideLength / 2 ? whole.Line(chordDirection) : (centre, direction);
    }

    // Total least squares for a line through points, summed in double so the spread across a long side does not
    // cancel away.
    private struct LineFit
    {
        private double _x, _y, _xx, _xy, _yy;

        public int Count { get; private set; }

        public void Add(Vector2 point)
        {
            _x += point.X;
            _y += point.Y;
            _xx += (double)point.X * point.X;
            _xy += (double)point.X * point.Y;
            _yy += (double)point.Y * point.Y;
            Count++;
        }

        // The line's centre and its direction, pointing along the given one.
        public readonly (Vector2 Centre, Vector2 Direction) Line(Vector2 along)
        {
            double x = _x / Count;
            double y = _y / Count;
            double xx = _xx / Count - x * x;
            double xy = _xy / Count - x * y;
            double yy = _yy / Count - y * y;
            double angle = Math.Atan2(2 * xy, xx - yy) / 2;
            var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            return (new Vector2((float)x, (float)y), Vector2.Dot(direction, along) < 0 ? -direction : direction);
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // The samples and the crossed edges of one trace. Sample (i, j) is the centre of pixel (i - 1, j - 1); cell
    // (i, j) has samples (i, j) and (i + 1, j + 1) at its corners. Edges are numbered horizontal first, row by row.
    private readonly struct Field(byte[] levels, int columns, int horizontalEdges, ulong[] visited)
    {
        public int Case(int i, int j)
        {
            int k = j * columns + i;
            return (levels[k] > Level ? 8 : 0)
                   | (levels[k + 1] > Level ? 4 : 0)
                   | (levels[k + columns + 1] > Level ? 2 : 0)
                   | (levels[k + columns] > Level ? 1 : 0);
        }

        public int Edge(int i, int j, int side)
        {
            return side switch
            {
                Top => j * (columns - 1) + i,
                Bottom => (j + 1) * (columns - 1) + i,
                Left => horizontalEdges + j * columns + i,
                _ => horizontalEdges + j * columns + i + 1,
            };
        }

        public bool IsVisited(int edge) => (visited[edge >> 6] & (1UL << (edge & 63))) != 0;

        // Walks one contour from the given cell and side until it is back where it started, keeping the inside on
        // the right. Every step marks a new edge, so the walk ends.
        public void Follow(int i, int j, int entry, List<Vector2> points)
        {
            int edge = Edge(i, j, entry);
            while (!IsVisited(edge))
            {
                visited[edge >> 6] |= 1UL << (edge & 63);
                points.Add(Crossing(edge));
                int exit = Exit(Case(i, j), entry, IsSaddleJoined(i, j));
                switch (exit)
                {
                    case Top:
                        j--;
                        entry = Bottom;
                        break;
                    case Right:
                        i++;
                        entry = Left;
                        break;
                    case Bottom:
                        j++;
                        entry = Top;
                        break;
                    case Left:
                        i--;
                        entry = Right;
                        break;
                    default:
                        return;
                }

                edge = Edge(i, j, entry);
            }
        }

        // Whether the two inside corners of a saddle cell connect, decided by the cell's mean. A tie connects them,
        // so pixels that touch only at a corner stay one shape.
        private bool IsSaddleJoined(int i, int j)
        {
            int k = j * columns + i;
            return levels[k] + levels[k + 1] + levels[k + columns] + levels[k + columns + 1] >= 4 * Level;
        }

        private Vector2 Crossing(int edge)
        {
            if (edge < horizontalEdges)
            {
                int j = edge / (columns - 1);
                int i = edge - j * (columns - 1);
                int k = j * columns + i;
                float t = (Level - levels[k]) / (levels[k + 1] - levels[k]);
                return new Vector2(i + t - 0.5f, j - 0.5f);
            }
            else
            {
                int index = edge - horizontalEdges;
                int j = index / columns;
                int i = index - j * columns;
                int k = j * columns + i;
                float t = (Level - levels[k]) / (levels[k + columns] - levels[k]);
                return new Vector2(i - 0.5f, j + t - 0.5f);
            }
        }

        private static int Exit(int shape, int entry, bool joined)
        {
            return (shape, entry) switch
            {
                (1, Left) => Bottom,
                (2, Bottom) => Right,
                (3, Left) => Right,
                (4, Right) => Top,
                (5, Left) => joined ? Top : Bottom,
                (5, Right) => joined ? Bottom : Top,
                (6, Bottom) => Top,
                (7, Left) => Top,
                (8, Top) => Left,
                (9, Top) => Bottom,
                (10, Top) => joined ? Right : Left,
                (10, Bottom) => joined ? Left : Right,
                (11, Top) => Right,
                (12, Right) => Left,
                (13, Right) => Bottom,
                (14, Bottom) => Left,
                _ => -1,
            };
        }
    }
}
