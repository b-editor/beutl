using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed partial class StillRenderer
{
    internal static StillFrameVisibilityAnalysis AnalyzeFrameVisibility(Bitmap bitmap)
    {
        const int nearBlackThreshold = 8;
        const int maxForegroundDeltaThreshold = 12;

        int totalPixels = bitmap.Width * bitmap.Height;
        if (totalPixels == 0)
        {
            return EmptyAnalysis("Rendered frame has no pixels.", nearBlackThreshold, maxForegroundDeltaThreshold);
        }

        LumaPixelReader reader = GetLumaPixelReader(bitmap);
        int visiblePixels = 0;
        int minLuma = byte.MaxValue;
        int maxLuma = byte.MinValue;
        double totalLuma = 0;
        double totalLumaSquared = 0;
        double borderLuma = 0;
        int borderPixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixelOffset = x * bitmap.BytesPerPixel;
                reader.Read(row, pixelOffset, out byte red, out byte green, out byte blue);
                int maxColorByte = Math.Max(red, Math.Max(green, blue));
                int luma = Rec709Luma(red, green, blue);
                if (maxColorByte > nearBlackThreshold)
                {
                    visiblePixels++;
                }

                minLuma = Math.Min(minLuma, luma);
                maxLuma = Math.Max(maxLuma, luma);
                totalLuma += luma;
                totalLumaSquared += luma * luma;
                if (IsBorderPixel(bitmap.Width, bitmap.Height, x, y))
                {
                    borderLuma += luma;
                    borderPixels++;
                }
            }
        }

        double visibleRatio = visiblePixels / (double)totalPixels;
        double meanLuma = totalLuma / totalPixels;
        double variance = Math.Max(0, totalLumaSquared / totalPixels - meanLuma * meanLuma);
        double lumaStdDev = Math.Sqrt(variance);
        double backgroundLuma = borderPixels == 0 ? meanLuma : borderLuma / borderPixels;
        int lumaRange = maxLuma - minLuma;
        int foregroundDeltaThreshold = Math.Clamp(lumaRange / 3, 4, maxForegroundDeltaThreshold);
        ForegroundCoverage coverage = MeasureForeground(
            bitmap, (r, g, b) => Math.Abs(Rec709Luma(r, g, b) - backgroundLuma) > foregroundDeltaThreshold);

        return new StillFrameVisibilityAnalysis(
            totalPixels,
            visiblePixels,
            visibleRatio,
            coverage.ForegroundPixels,
            coverage.ForegroundPixelRatio,
            coverage.OccupiedBoundsRatio,
            coverage.MaxQuadrantForegroundRatio,
            coverage.Left,
            coverage.Top,
            coverage.Right,
            coverage.Bottom,
            minLuma,
            maxLuma,
            meanLuma,
            lumaStdDev,
            backgroundLuma,
            nearBlackThreshold,
            foregroundDeltaThreshold,
            []);
    }

    internal readonly record struct ForegroundCoverage(
        int ForegroundPixels,
        double ForegroundPixelRatio,
        double OccupiedBoundsRatio,
        double MaxQuadrantForegroundRatio,
        int Left,
        int Top,
        int Right,
        int Bottom);

    // Counts the pixels whose sRGB color isForeground accepts, with their bounds and the share of them in the
    // busiest quadrant. FrameDifferenceAnalyzer measures its frames with it too.
    internal static ForegroundCoverage MeasureForeground(Bitmap bitmap, Func<byte, byte, byte, bool> isForeground)
    {
        LumaPixelReader reader = GetLumaPixelReader(bitmap);
        int totalPixels = bitmap.Width * bitmap.Height;
        int foregroundPixels = 0;
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = -1;
        int bottom = -1;
        Span<int> quadrants = stackalloc int[4];

        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                reader.Read(row, x * bitmap.BytesPerPixel, out byte r, out byte g, out byte b);
                if (!isForeground(r, g, b))
                {
                    continue;
                }

                foregroundPixels++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                int quadrant = (x < bitmap.Width / 2 ? 0 : 1) + (y < bitmap.Height / 2 ? 0 : 2);
                quadrants[quadrant]++;
            }
        }

        if (foregroundPixels == 0)
        {
            return new ForegroundCoverage(0, 0, 0, 0, 0, 0, 0, 0);
        }

        int maxQuadrantPixels = 0;
        for (int i = 0; i < quadrants.Length; i++)
        {
            maxQuadrantPixels = Math.Max(maxQuadrantPixels, quadrants[i]);
        }

        int boundsPixels = (right - left + 1) * (bottom - top + 1);
        return new ForegroundCoverage(
            foregroundPixels,
            foregroundPixels / (double)totalPixels,
            boundsPixels / (double)totalPixels,
            maxQuadrantPixels / (double)foregroundPixels,
            left,
            top,
            right,
            bottom);
    }

    private static StillFrameVisibilityAnalysis EmptyAnalysis(
        string warning,
        int visibilityThreshold,
        int foregroundDeltaThreshold)
    {
        return new StillFrameVisibilityAnalysis(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            visibilityThreshold,
            foregroundDeltaThreshold,
            [warning]);
    }

    internal static NormalizedFocalPoint EstimateFocalPoint(
        Scene scene,
        RenderedFrameAnalysis frame,
        StillFrameVisibilityAnalysis visibility)
    {
        if (TryEstimateTextFocalPoint(scene, frame.TextBounds, out NormalizedFocalPoint textFocalPoint))
        {
            return textFocalPoint;
        }

        return EstimateForegroundFocalPoint(frame.Bitmap, visibility);
    }

    private static bool TryEstimateTextFocalPoint(
        Scene scene,
        IReadOnlyList<RenderedTextBounds> textBounds,
        out NormalizedFocalPoint focalPoint)
    {
        RenderedTextBounds? best = textBounds
            .Where(item => item.TextBlock.IsEnabled)
            .Where(item => !string.IsNullOrWhiteSpace(item.TextBlock.Text.CurrentValue))
            .Where(item => item.Bounds.Width >= 4 && item.Bounds.Height >= 4)
            .OrderByDescending(item => item.Element.ZIndex)
            .ThenByDescending(item => item.Bounds.Width * item.Bounds.Height)
            .FirstOrDefault();
        if (best is null)
        {
            focalPoint = new NormalizedFocalPoint(0.5, 0.5);
            return false;
        }

        double width = scene.FrameSize.Width > 0 ? scene.FrameSize.Width : best.Bounds.Right;
        double height = scene.FrameSize.Height > 0 ? scene.FrameSize.Height : best.Bounds.Bottom;
        focalPoint = new NormalizedFocalPoint(
            NormalizeUnit(best.Bounds.Center.X, width),
            NormalizeUnit(best.Bounds.Center.Y, height));
        return true;
    }

    private static NormalizedFocalPoint EstimateForegroundFocalPoint(
        Bitmap bitmap,
        StillFrameVisibilityAnalysis visibility)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || visibility.ForegroundPixels <= 0)
        {
            return new NormalizedFocalPoint(0.5, 0.5);
        }

        LumaPixelReader reader = GetLumaPixelReader(bitmap);
        int totalPixels = bitmap.Width * bitmap.Height;
        var state = new byte[totalPixels];
        var lumaByPixel = new byte[totalPixels];
        double globalWeight = 0;
        double globalX = 0;
        double globalY = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int index = (y * bitmap.Width) + x;
                int offset = x * bitmap.BytesPerPixel;
                reader.Read(row, offset, out byte pr, out byte pg, out byte pb);
                int luma = Rec709Luma(pr, pg, pb);
                lumaByPixel[index] = (byte)Math.Clamp(luma, 0, byte.MaxValue);
                double delta = Math.Abs(luma - visibility.BackgroundLuma);
                if (delta <= visibility.ForegroundDeltaThreshold)
                {
                    continue;
                }

                state[index] = 1;
                double weight = Math.Max(1, delta);
                globalWeight += weight;
                globalX += (x + 0.5) * weight;
                globalY += (y + 0.5) * weight;
            }
        }

        if (globalWeight <= 0)
        {
            return FocalFromVisibilityBounds(bitmap, visibility);
        }

        double globalCentroidX = globalX / globalWeight;
        double globalCentroidY = globalY / globalWeight;
        double bestWeight = 0;
        double bestX = 0;
        double bestY = 0;
        var stack = new List<int>();
        for (int index = 0; index < state.Length; index++)
        {
            if (state[index] != 1)
            {
                continue;
            }

            double componentWeight = 0;
            double componentX = 0;
            double componentY = 0;
            stack.Clear();
            stack.Add(index);
            state[index] = 2;
            while (stack.Count > 0)
            {
                int current = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                int x = current % bitmap.Width;
                int y = current / bitmap.Width;
                double weight = Math.Max(1, Math.Abs(lumaByPixel[current] - visibility.BackgroundLuma));
                componentWeight += weight;
                componentX += (x + 0.5) * weight;
                componentY += (y + 0.5) * weight;

                AddForegroundNeighbor(x - 1, y);
                AddForegroundNeighbor(x + 1, y);
                AddForegroundNeighbor(x, y - 1);
                AddForegroundNeighbor(x, y + 1);
            }

            if (componentWeight > bestWeight)
            {
                bestWeight = componentWeight;
                bestX = componentX / componentWeight;
                bestY = componentY / componentWeight;
            }
        }

        double focalX = bestWeight > 0 ? (globalCentroidX * 0.35) + (bestX * 0.65) : globalCentroidX;
        double focalY = bestWeight > 0 ? (globalCentroidY * 0.35) + (bestY * 0.65) : globalCentroidY;
        return new NormalizedFocalPoint(
            NormalizeUnit(focalX, bitmap.Width),
            NormalizeUnit(focalY, bitmap.Height));

        void AddForegroundNeighbor(int x, int y)
        {
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
            {
                return;
            }

            int neighbor = (y * bitmap.Width) + x;
            if (state[neighbor] != 1)
            {
                return;
            }

            state[neighbor] = 2;
            stack.Add(neighbor);
        }
    }

    private static NormalizedFocalPoint FocalFromVisibilityBounds(
        Bitmap bitmap,
        StillFrameVisibilityAnalysis visibility)
    {
        if (visibility.Right <= visibility.Left || visibility.Bottom <= visibility.Top)
        {
            return new NormalizedFocalPoint(0.5, 0.5);
        }

        double x = (visibility.Left + visibility.Right + 1) / 2d;
        double y = (visibility.Top + visibility.Bottom + 1) / 2d;
        return new NormalizedFocalPoint(
            NormalizeUnit(x, bitmap.Width),
            NormalizeUnit(y, bitmap.Height));
    }

    private static double NormalizeUnit(double value, double denominator)
    {
        if (!double.IsFinite(value) || !double.IsFinite(denominator) || denominator <= 0)
        {
            return 0.5;
        }

        return Math.Round(Math.Clamp(value / denominator, 0, 1), 4, MidpointRounding.AwayFromZero);
    }

    private static bool IsBorderPixel(int width, int height, int x, int y)
    {
        const int borderWidth = 2;
        return x < borderWidth
               || y < borderWidth
               || x >= width - borderWidth
               || y >= height - borderWidth;
    }

    // The renderer hands back RgbaF16 — linear, premultiplied half-floats — so reading bytes
    // positionally yields a number that looks like a luma and is not one. The reader decodes to
    // sRGB-encoded 0..255 so reported values match the PNG a human looks at.
    internal readonly struct LumaPixelReader
    {
        public void Read(Span<byte> row, int pixelOffset, out byte r, out byte g, out byte b)
        {
            byte[] table = LinearHalfEncodeTable;
            r = table[BitConverter.ToUInt16(row.Slice(pixelOffset, 2))];
            g = table[BitConverter.ToUInt16(row.Slice(pixelOffset + 2, 2))];
            b = table[BitConverter.ToUInt16(row.Slice(pixelOffset + 4, 2))];
        }
    }

    private static byte LinearToSrgbByte(float linear)
    {
        if (!float.IsFinite(linear))
        {
            return 0;
        }

        double c = Math.Clamp(linear, 0, 1);
        double encoded = c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;
        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }

    // Normal render snapshots are linear RgbaF16. Cache the transfer function by raw half bits so
    // each full-frame visibility/focal pass performs table reads instead of millions of Math.Pow calls.
    private static byte[]? s_linearHalfEncodeTable;

    private static byte[] LinearHalfEncodeTable => s_linearHalfEncodeTable ??= BuildLinearHalfEncodeTable();

    private static byte[] BuildLinearHalfEncodeTable()
    {
        var table = new byte[ushort.MaxValue + 1];
        for (int bits = 0; bits <= ushort.MaxValue; bits++)
        {
            table[bits] = LinearToSrgbByte((float)BitConverter.UInt16BitsToHalf((ushort)bits));
        }

        return table;
    }

    private static int Rec709Luma(byte r, byte g, byte b)
    {
        return (int)Math.Round((0.2126 * r) + (0.7152 * g) + (0.0722 * b));
    }

    // Render snapshots are always premultiplied RgbaF16.
    internal static LumaPixelReader GetLumaPixelReader(Bitmap bitmap)
    {
        if (bitmap.ColorType is not (BitmapColorType.RgbaF16 or BitmapColorType.RgbaF16Clamped)
            || bitmap.BytesPerPixel < 8
            || bitmap.AlphaType != BitmapAlphaType.Premul)
        {
            throw new NotSupportedException(
                $"Frame analysis reads premultiplied RgbaF16 render snapshots, not {bitmap.ColorType} with {bitmap.AlphaType} alpha.");
        }

        return default;
    }
}
