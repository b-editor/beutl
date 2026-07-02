using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record MotionVariationSample(
    string Time,
    int Width,
    int Height);

public sealed record MotionVariationPair(
    string FromTime,
    string ToTime,
    int ChangedPixels,
    int TotalPixels,
    double ChangedPixelRatio,
    double MeanAbsoluteDelta);

public sealed record MotionFrameCoverage(
    string Time,
    int ForegroundPixels,
    int TotalPixels,
    double ForegroundPixelRatio,
    double OccupiedBoundsRatio,
    double MaxQuadrantForegroundRatio,
    int Left,
    int Top,
    int Right,
    int Bottom);

public sealed record MotionVariationResponse(
    bool PassesMinimumMotion,
    bool PassesTemporalMotion,
    bool PassesFrameCoverage,
    string Verdict,
    double MinimumChangedPixelRatio,
    double AverageChangedPixelRatio,
    IReadOnlyList<MotionVariationSample> Samples,
    IReadOnlyList<MotionVariationPair> PairVariations,
    IReadOnlyList<MotionFrameCoverage> FrameCoverage,
    IReadOnlyList<string> ReviewNotes);

public sealed class MotionVariationAnalyzer(StillRenderer stillRenderer)
{
    public async ValueTask<MotionVariationResponse> AnalyzeAsync(
        Scene scene,
        IReadOnlyList<TimeSpan> sampleTimes,
        float renderScale,
        double minChangedPixelRatio,
        int pixelDeltaThreshold,
        double minOccupiedBoundsRatio,
        double maxSingleQuadrantForegroundRatio,
        int foregroundLumaThreshold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sampleTimes);
        if (sampleTimes.Count < 2)
        {
            throw new ArgumentException("At least two sample times are required.", nameof(sampleTimes));
        }

        double normalizedMinimumRatio = double.IsFinite(minChangedPixelRatio)
            ? Math.Clamp(minChangedPixelRatio, 0, 1)
            : 0.02;
        int normalizedDeltaThreshold = Math.Clamp(pixelDeltaThreshold, 1, 1020);
        double normalizedMinOccupiedBoundsRatio = double.IsFinite(minOccupiedBoundsRatio)
            ? Math.Clamp(minOccupiedBoundsRatio, 0, 1)
            : 0.35;
        double normalizedMaxSingleQuadrantRatio = double.IsFinite(maxSingleQuadrantForegroundRatio)
            ? Math.Clamp(maxSingleQuadrantForegroundRatio, 0, 1)
            : 0.9;
        int normalizedForegroundThreshold = Math.Clamp(foregroundLumaThreshold, 0, byte.MaxValue);

        var frames = new List<(TimeSpan Time, Bitmap Bitmap)>(sampleTimes.Count);
        try
        {
            foreach (TimeSpan time in sampleTimes)
            {
                frames.Add((
                    time,
                    await stillRenderer.RenderBitmapAsync(
                        scene,
                        time,
                        renderScale,
                        cancellationToken).ConfigureAwait(false)));
            }

            MotionVariationSample[] samples = frames
                .Select(frame => new MotionVariationSample(
                    frame.Time.ToString("c"),
                    frame.Bitmap.Width,
                    frame.Bitmap.Height))
                .ToArray();
            MotionVariationPair[] variations = frames
                .Zip(frames.Skip(1), (previous, next) => Compare(previous, next, normalizedDeltaThreshold))
                .ToArray();
            MotionFrameCoverage[] coverage = frames
                .Select(frame => AnalyzeFrameCoverage(frame, normalizedForegroundThreshold))
                .ToArray();

            double minimumRatio = variations.Min(item => item.ChangedPixelRatio);
            double averageRatio = variations.Average(item => item.ChangedPixelRatio);
            bool passesTemporal = minimumRatio >= normalizedMinimumRatio;
            int sustainedSampleCount = Math.Max(2, (coverage.Length + 1) / 2);
            int confinedSamples = coverage.Count(item =>
                item.ForegroundPixels > 0
                && item.OccupiedBoundsRatio <= normalizedMinOccupiedBoundsRatio
                && item.MaxQuadrantForegroundRatio >= normalizedMaxSingleQuadrantRatio);
            int sparseSamples = coverage.Count(item =>
                item.ForegroundPixels == 0 || item.OccupiedBoundsRatio < 0.12);
            bool passesCoverage = confinedSamples < sustainedSampleCount && sparseSamples < sustainedSampleCount;
            bool passes = passesTemporal && passesCoverage;
            string verdict = passes
                ? "motion-variation-ok"
                : !passesTemporal
                    ? "low-motion-variation"
                    : "poor-frame-coverage";

            List<string> notes = [];
            if (passesTemporal)
            {
                notes.Add("Temporal variation meets the requested changed-pixel threshold.");
            }
            else
            {
                notes.Add("Temporal variation is low between at least one adjacent sample.");
                notes.Add("Revise the edit with stronger phase changes, more animated properties, or denser foreground/background motion.");
            }

            if (passesCoverage)
            {
                notes.Add("Frame coverage is not persistently confined to one small quadrant.");
            }
            else if (confinedSamples >= sustainedSampleCount)
            {
                notes.Add("Visible content stays confined to one small quadrant for too many sampled frames.");
                notes.Add("Revise the composition so background, foreground motion, accents, or typography use more of the frame.");
            }
            else
            {
                notes.Add("Too many sampled frames have little or no visible foreground coverage.");
                notes.Add("Add stronger visible structure before exporting.");
            }

            return new MotionVariationResponse(
                passes,
                passesTemporal,
                passesCoverage,
                verdict,
                minimumRatio,
                averageRatio,
                samples,
                variations,
                coverage,
                notes);
        }
        finally
        {
            foreach ((_, Bitmap bitmap) in frames)
            {
                bitmap.Dispose();
            }
        }
    }

    private static MotionVariationPair Compare(
        (TimeSpan Time, Bitmap Bitmap) previous,
        (TimeSpan Time, Bitmap Bitmap) next,
        int pixelDeltaThreshold)
    {
        if (previous.Bitmap.Width != next.Bitmap.Width || previous.Bitmap.Height != next.Bitmap.Height)
        {
            throw new InvalidOperationException("Motion variation samples must have matching frame sizes.");
        }

        if (previous.Bitmap.BytesPerPixel != next.Bitmap.BytesPerPixel)
        {
            throw new InvalidOperationException("Motion variation samples must have matching pixel formats.");
        }

        int bytesPerPixel = previous.Bitmap.BytesPerPixel;
        int width = previous.Bitmap.Width;
        int height = previous.Bitmap.Height;
        int totalPixels = width * height;
        int changedPixels = 0;
        long totalDelta = 0;

        for (int y = 0; y < height; y++)
        {
            Span<byte> first = previous.Bitmap.GetRow(y);
            Span<byte> second = next.Bitmap.GetRow(y);
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = x * bytesPerPixel;
                int pixelDelta = 0;
                for (int channel = 0; channel < bytesPerPixel; channel++)
                {
                    pixelDelta += Math.Abs(first[pixelOffset + channel] - second[pixelOffset + channel]);
                }

                totalDelta += pixelDelta;
                if (pixelDelta > pixelDeltaThreshold)
                {
                    changedPixels++;
                }
            }
        }

        double changedRatio = totalPixels == 0 ? 0 : changedPixels / (double)totalPixels;
        double meanDelta = totalPixels == 0 || bytesPerPixel == 0
            ? 0
            : totalDelta / (double)(totalPixels * bytesPerPixel * byte.MaxValue);

        return new MotionVariationPair(
            previous.Time.ToString("c"),
            next.Time.ToString("c"),
            changedPixels,
            totalPixels,
            changedRatio,
            meanDelta);
    }

    private static MotionFrameCoverage AnalyzeFrameCoverage(
        (TimeSpan Time, Bitmap Bitmap) frame,
        int foregroundLumaThreshold)
    {
        int bytesPerPixel = frame.Bitmap.BytesPerPixel;
        int width = frame.Bitmap.Width;
        int height = frame.Bitmap.Height;
        int totalPixels = width * height;
        int colorByteCount = GetColorByteCount(frame.Bitmap.ColorType, bytesPerPixel);
        int foregroundPixels = 0;
        int left = width;
        int top = height;
        int right = -1;
        int bottom = -1;
        Span<int> quadrants = stackalloc int[4];

        for (int y = 0; y < height; y++)
        {
            Span<byte> row = frame.Bitmap.GetRow(y);
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = x * bytesPerPixel;
                if (!IsForeground(row, pixelOffset, colorByteCount, foregroundLumaThreshold))
                {
                    continue;
                }

                foregroundPixels++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                int quadrant = (x < width / 2 ? 0 : 1) + (y < height / 2 ? 0 : 2);
                quadrants[quadrant]++;
            }
        }

        double foregroundRatio = totalPixels == 0 ? 0 : foregroundPixels / (double)totalPixels;
        double occupiedBoundsRatio = 0;
        double maxQuadrantRatio = 0;
        if (foregroundPixels > 0)
        {
            int boundsPixels = (right - left + 1) * (bottom - top + 1);
            occupiedBoundsRatio = totalPixels == 0 ? 0 : boundsPixels / (double)totalPixels;
            int maxQuadrantPixels = 0;
            for (int i = 0; i < quadrants.Length; i++)
            {
                maxQuadrantPixels = Math.Max(maxQuadrantPixels, quadrants[i]);
            }

            maxQuadrantRatio = maxQuadrantPixels / (double)foregroundPixels;
        }
        else
        {
            left = top = right = bottom = 0;
        }

        return new MotionFrameCoverage(
            frame.Time.ToString("c"),
            foregroundPixels,
            totalPixels,
            foregroundRatio,
            occupiedBoundsRatio,
            maxQuadrantRatio,
            left,
            top,
            right,
            bottom);
    }

    private static bool IsForeground(
        Span<byte> row,
        int pixelOffset,
        int colorByteCount,
        int foregroundLumaThreshold)
    {
        if (colorByteCount <= 0)
        {
            return false;
        }

        int maxColorByte = 0;
        for (int channel = 0; channel < colorByteCount; channel++)
        {
            maxColorByte = Math.Max(maxColorByte, row[pixelOffset + channel]);
        }

        return maxColorByte > foregroundLumaThreshold;
    }

    private static int GetColorByteCount(BitmapColorType colorType, int bytesPerPixel)
    {
        return colorType switch
        {
            BitmapColorType.Alpha8 => bytesPerPixel,
            BitmapColorType.Rgb565 => bytesPerPixel,
            BitmapColorType.Argb4444 => bytesPerPixel,
            BitmapColorType.Rgba8888 => Math.Min(3, bytesPerPixel),
            BitmapColorType.Rgb888x => Math.Min(3, bytesPerPixel),
            BitmapColorType.Bgra8888 => Math.Min(3, bytesPerPixel),
            BitmapColorType.Rgba1010102 => bytesPerPixel,
            BitmapColorType.Bgra1010102 => bytesPerPixel,
            BitmapColorType.Rgb101010x => bytesPerPixel,
            BitmapColorType.Bgr101010x => bytesPerPixel,
            BitmapColorType.Bgr101010xXR => bytesPerPixel,
            BitmapColorType.Gray8 => Math.Min(1, bytesPerPixel),
            BitmapColorType.RgbaF16 => Math.Min(6, bytesPerPixel),
            BitmapColorType.RgbaF16Clamped => Math.Min(6, bytesPerPixel),
            BitmapColorType.RgbaF32 => Math.Min(12, bytesPerPixel),
            BitmapColorType.Rg88 => bytesPerPixel,
            BitmapColorType.AlphaF16 => bytesPerPixel,
            BitmapColorType.RgF16 => bytesPerPixel,
            BitmapColorType.Alpha16 => bytesPerPixel,
            BitmapColorType.Rg1616 => bytesPerPixel,
            BitmapColorType.Rgba16161616 => Math.Min(6, bytesPerPixel),
            BitmapColorType.Srgba8888 => Math.Min(3, bytesPerPixel),
            BitmapColorType.R8Unorm => Math.Min(1, bytesPerPixel),
            _ => Math.Min(3, bytesPerPixel)
        };
    }
}
