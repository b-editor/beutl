using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Effects;

public readonly struct Contours(PooledList<PooledList<PixelPoint>> contours) : IDisposable
{
    public int Count => List.Count;

    public ReadOnlySpan<PixelPoint> this[int index] => List[index].Span;

    public PooledList<PooledList<PixelPoint>> List { get; } = contours;

    public PooledList<PooledList<PixelPoint>>.Enumerator GetEnumerator()
    {
        return List.GetEnumerator();
    }

    public void Dispose()
    {
        foreach (var contour in List)
        {
            contour.Dispose();
        }
        List.Dispose();
    }
}

/// <summary>
/// Finds contours in the alpha channel of a BGRA bitmap using Moore boundary tracing.
/// Produces results equivalent to OpenCV's findContours with RetrievalModes.List
/// and ContourApproximationModes.ApproxSimple.
/// </summary>
public static class ContourTracer
{
    // 8-connected neighbor offsets (clockwise starting from East)
    // 0=E, 1=SE, 2=S, 3=SW, 4=W, 5=NW, 6=N, 7=NE
    private static readonly int[] s_dx = [1, 1, 0, -1, -1, -1, 0, 1];
    private static readonly int[] s_dy = [0, 1, 1, 1, 0, -1, -1, -1];

    // 4-connected neighbor offsets (for background flood fill)
    private static readonly int[] s_dx4 = [1, 0, -1, 0];
    private static readonly int[] s_dy4 = [0, 1, 0, -1];

    /// <summary>
    /// Finds contours in the alpha channel of the bitmap.
    /// Returns a list of contours, each contour being an array of (X, Y) points
    /// with collinear points removed (ApproxSimple).
    /// </summary>
    public static Contours FindContours(Bitmap bitmap)
    {
        FindContoursWithHierarchy(bitmap, out var contours, out var parentIndices);
        parentIndices.Dispose();
        return contours;
    }

    /// <summary>
    /// Finds contours with hierarchy information (equivalent to OpenCV's Tree retrieval mode).
    /// Each contour has a parent index (-1 if top-level outer contour).
    /// Hole contours reference their enclosing outer contour as parent.
    /// </summary>
    public static void FindContoursWithHierarchy(
        Bitmap bitmap,
        out Contours contours,
        out PooledList<int> parentIndices)
    {
        using var alphaBitmap = bitmap.Convert(BitmapColorType.Alpha8);
        FindContoursCore(alphaBitmap, out var contoursList, out parentIndices);
        contours = new Contours(contoursList);
    }

    private static void FindContoursCore(
        Bitmap bitmap,
        out PooledList<PooledList<PixelPoint>> contours,
        out PooledList<int> parentIndices)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        // Create binary image: true = foreground (alpha > 0)
        using var fg = new PooledArray<bool>(height * width);
        var fgSpan = fg.Span;
        fgSpan.Fill(false);
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = bitmap.GetRow<byte>(y);
            int offset = y * width;
            for (int x = 0; x < width; x++)
            {
                fgSpan[offset + x] = row[x] > 0;
            }
        }

        contours = [];
        parentIndices = [];

        // Map pixel → outer contour index (for determining hole parents).
        // Also used to skip fg pixels already belonging to a traced outer contour,
        // which prevents inner walls of ring-shaped regions from being re-traced.
        using var outerContourMap = new PooledArray<int>(height * width);
        var outerContourMapSpan = outerContourMap.Span;
        outerContourMapSpan.Fill(-1);

        // Phase 1: Find outer contours.
        // After each outer contour is traced, flood-fill the entire connected fg component
        // so that inner walls (e.g. the inner right wall of a hollow square) are marked
        // and not mistaken for additional outer-contour starting points.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (!fgSpan[idx] || outerContourMapSpan[idx] >= 0)
                    continue;

                bool bgLeft = x == 0 || !fgSpan[idx - 1];
                if (bgLeft)
                {
                    int contourIdx = contours.Count;
                    using var contour = TraceBorder(fgSpan, width, height, x, y, startDir: 4);
                    foreach (PixelPoint point in contour)
                        outerContourMapSpan[point.Y * width + point.X] = contourIdx;

                    contours.Add(ApproxSimple(contour));
                    parentIndices.Add(-1);

                    // Flood-fill the whole connected fg component seeded from all contour
                    // pixels so interior pixels (not on the traced boundary) are also marked.
                    // This prevents inner walls of ring-shaped regions from being re-traced.
                    FloodFillContourMap(fgSpan, outerContourMapSpan, width, height, contour, contourIdx);
                }
            }
        }

        // Identify external background pixels (connected to image boundary)
        using var externalBg = FloodFillExternalBackground(fgSpan, width, height);
        var externalBgSpan = externalBg.Span;

        // Phase 2: Find hole contours.
        // For each internal (non-external) background connected component, flood-fill it to
        // find all fg pixels 8-adjacent to that component (adjFg). Tracing with adjFg as the
        // mask prevents the tracer from "escaping" from the inner wall to the outer wall of
        // ring-shaped fg regions (e.g. the outer frame of 国).
        using var holeVisited = new PooledArray<bool>(height * width);
        Span<bool> holeVisitedSpan = holeVisited.Span;
        holeVisitedSpan.Clear();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (fgSpan[idx] || externalBgSpan[idx] || holeVisitedSpan[idx])
                    continue;

                // New internal bg region found. Flood-fill it and collect adjacent fg pixels.
                using var adjFgArr = new PooledArray<bool>(height * width);
                Span<bool> adjFgSpan = adjFgArr.Span;
                adjFgSpan.Clear();
                FloodFillAndMarkAdjFg(fgSpan, externalBgSpan, holeVisitedSpan, adjFgSpan, width, height, x, y);

                // The fg pixel directly above the topmost-leftmost bg pixel starts the trace.
                if (y > 0 && fgSpan[(y - 1) * width + x])
                {
                    int parentIdx = outerContourMapSpan[(y - 1) * width + x];
                    using var rawContour = TraceBorder(adjFgSpan, width, height, x, y - 1, startDir: 2);
                    contours.Add(ApproxSimple(rawContour));
                    parentIndices.Add(parentIdx);
                }
            }
        }
    }

    /// <summary>
    /// Flood fills the connected fg component seeded from all pixels in
    /// <paramref name="seeds"/>, assigning <paramref name="label"/> to every
    /// unassigned fg pixel reachable from any seed.
    /// Seeding from the full boundary list (rather than a single point) ensures
    /// interior pixels surrounded by already-labeled boundary pixels are reached.
    /// </summary>
    private static void FloodFillContourMap(
        Span<bool> fg, Span<int> contourMap, int width, int height,
        PooledList<PixelPoint> seeds, int label)
    {
        Queue<PixelPoint> queue = new();
        foreach (PixelPoint p in seeds)
            queue.Enqueue(p);

        while (queue.Count > 0)
        {
            PixelPoint c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = c.X + s_dx4[d];
                int ny = c.Y + s_dy4[d];
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    int nIdx = ny * width + nx;
                    if (fg[nIdx] && contourMap[nIdx] < 0)
                    {
                        contourMap[nIdx] = label;
                        queue.Enqueue(new PixelPoint(nx, ny));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Flood fills from all image boundary background pixels to identify external background.
    /// Uses 4-connectivity for background (complementary to 8-connected foreground).
    /// </summary>
    private static PooledArray<bool> FloodFillExternalBackground(Span<bool> fg, int width, int height)
    {
        var visited = new PooledArray<bool>(height * width);
        var visitedSpan = visited.Span;
        visitedSpan.Fill(false);
        Queue<PixelPoint> queue = new();

        // Seed from all edge background pixels
        for (int x = 0; x < width; x++)
        {
            if (!fg[x] && !visitedSpan[x])
            {
                visitedSpan[x] = true;
                queue.Enqueue(new PixelPoint(x, 0));
            }

            int bottomIdx = (height - 1) * width + x;
            if (!fg[bottomIdx] && !visitedSpan[bottomIdx])
            {
                visitedSpan[bottomIdx] = true;
                queue.Enqueue(new PixelPoint(x, height - 1));
            }
        }

        for (int y = 1; y < height - 1; y++)
        {
            int leftIdx = y * width;
            if (!fg[leftIdx] && !visitedSpan[leftIdx])
            {
                visitedSpan[leftIdx] = true;
                queue.Enqueue(new PixelPoint(0, y));
            }

            int rightIdx = y * width + width - 1;
            if (!fg[rightIdx] && !visitedSpan[rightIdx])
            {
                visitedSpan[rightIdx] = true;
                queue.Enqueue(new PixelPoint(width - 1, y));
            }
        }

        // BFS flood fill
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = c.X + s_dx4[d];
                int ny = c.Y + s_dy4[d];
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    int nIdx = ny * width + nx;
                    if (!fg[nIdx] && !visitedSpan[nIdx])
                    {
                        visitedSpan[nIdx] = true;
                        queue.Enqueue(new PixelPoint(nx, ny));
                    }
                }
            }
        }

        return visited;
    }

    /// <summary>
    /// Traces a contour border starting from (startX, startY) using Moore neighborhood tracing.
    /// </summary>
    private static PooledList<PixelPoint> TraceBorder(
        Span<bool> fg, int width, int height,
        int startX, int startY, int startDir)
    {
        // Find the first foreground neighbor by scanning clockwise from startDir
        int firstDir = -1;
        int firstX = -1, firstY = -1;

        for (int i = 0; i < 8; i++)
        {
            int dir = (startDir + i) % 8;
            int nx = startX + s_dx[dir];
            int ny = startY + s_dy[dir];

            if (nx >= 0 && nx < width && ny >= 0 && ny < height && fg[ny * width + nx])
            {
                firstDir = dir;
                firstX = nx;
                firstY = ny;
                break;
            }
        }

        // Isolated pixel
        if (firstDir == -1)
        {
            return [new PixelPoint(startX, startY)];
        }

        PooledList<PixelPoint> contour = [new PixelPoint(startX, startY)];

        int curX = firstX, curY = firstY;
        int prevDir = firstDir;

        // Safety limit to prevent infinite loops in degenerate cases
        int maxIter = width * height * 2;
        int iter = 0;

        while (iter++ < maxIter)
        {
            // Check termination BEFORE adding: if we're back at start
            if (curX == startX && curY == startY)
            {
                // Verify next neighbor would be firstX, firstY
                int checkStart = ((prevDir + 4) % 8 + 1) % 8;
                for (int i = 0; i < 8; i++)
                {
                    int dir = (checkStart + i) % 8;
                    int nx = curX + s_dx[dir];
                    int ny = curY + s_dy[dir];
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && fg[ny * width + nx])
                    {
                        if (nx == firstX && ny == firstY)
                        {
                            return contour;
                        }

                        break;
                    }
                }
            }

            contour.Add(new PixelPoint(curX, curY));

            // Search for next border pixel
            int searchStart = ((prevDir + 4) % 8 + 1) % 8;
            int nextDir = -1;
            int nextX = -1, nextY = -1;

            for (int i = 0; i < 8; i++)
            {
                int dir = (searchStart + i) % 8;
                int nx = curX + s_dx[dir];
                int ny = curY + s_dy[dir];

                if (nx >= 0 && nx < width && ny >= 0 && ny < height && fg[ny * width + nx])
                {
                    nextDir = dir;
                    nextX = nx;
                    nextY = ny;
                    break;
                }
            }

            if (nextDir == -1)
            {
                break;
            }

            prevDir = nextDir;
            curX = nextX;
            curY = nextY;
        }

        return contour;
    }

    /// <summary>
    /// BFS flood fills an internal background region starting from (startX, startY).
    /// Marks all bg pixels in the component in <paramref name="visited"/> and marks
    /// all fg pixels 8-adjacent to the component in <paramref name="adjFg"/>.
    /// Using adjFg as the tracing mask prevents the Moore tracer from escaping from
    /// a hole's inner boundary to the outer boundary of a ring-shaped fg region.
    /// </summary>
    private static void FloodFillAndMarkAdjFg(
        Span<bool> fg, Span<bool> externalBg, Span<bool> visited, Span<bool> adjFg,
        int width, int height, int startX, int startY)
    {
        Queue<PixelPoint> queue = new();
        visited[startY * width + startX] = true;
        queue.Enqueue(new PixelPoint(startX, startY));

        while (queue.Count > 0)
        {
            PixelPoint c = queue.Dequeue();

            // Mark fg pixels 8-adjacent to this bg pixel
            for (int d = 0; d < 8; d++)
            {
                int nx = c.X + s_dx[d];
                int ny = c.Y + s_dy[d];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;
                int nIdx = ny * width + nx;
                if (fg[nIdx])
                    adjFg[nIdx] = true;
            }

            // BFS expand to 4-connected internal bg neighbors
            for (int d = 0; d < 4; d++)
            {
                int nx = c.X + s_dx4[d];
                int ny = c.Y + s_dy4[d];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;
                int nIdx = ny * width + nx;
                if (!fg[nIdx] && !externalBg[nIdx] && !visited[nIdx])
                {
                    visited[nIdx] = true;
                    queue.Enqueue(new PixelPoint(nx, ny));
                }
            }
        }
    }

    /// <summary>
    /// Applies ApproxSimple compression: removes intermediate points on horizontal,
    /// vertical, and diagonal segments, keeping only endpoints where direction changes.
    /// </summary>
    private static PooledList<PixelPoint> ApproxSimple(PooledList<PixelPoint> contour)
    {
        if (contour.Count <= 2)
        {
            return [.. contour];
        }

        PooledList<PixelPoint> compressed = [contour[0]];

        for (int i = 1; i < contour.Count - 1; i++)
        {
            int prevDx = contour[i].X - contour[i - 1].X;
            int prevDy = contour[i].Y - contour[i - 1].Y;
            int nextDx = contour[i + 1].X - contour[i].X;
            int nextDy = contour[i + 1].Y - contour[i].Y;

            if (prevDx != nextDx || prevDy != nextDy)
            {
                compressed.Add(contour[i]);
            }
        }

        compressed.Add(contour[^1]);

        // Check wrap-around: if last→first→second is collinear, remove first
        if (compressed.Count > 2)
        {
            int lastDx = compressed[0].X - compressed[^1].X;
            int lastDy = compressed[0].Y - compressed[^1].Y;
            int firstDx = compressed[1].X - compressed[0].X;
            int firstDy = compressed[1].Y - compressed[0].Y;

            if (lastDx == firstDx && lastDy == firstDy)
            {
                compressed.RemoveAt(0);
            }
        }

        return compressed;
    }
}
