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

public sealed record FrameDifferenceResponse(
    int PixelDeltaThreshold,
    int ForegroundLumaThreshold,
    double MinimumChangedPixelRatio,
    double AverageChangedPixelRatio,
    IReadOnlyList<MotionVariationSample> Samples,
    IReadOnlyList<MotionVariationPair> PairVariations,
    IReadOnlyList<MotionFrameCoverage> FrameCoverage);

public sealed class FrameDifferenceAnalyzer(StillRenderer stillRenderer)
{
    public async ValueTask<FrameDifferenceResponse> AnalyzeAsync(
        Scene scene,
        IReadOnlyList<TimeSpan> sampleTimes,
        float renderScale,
        int pixelDeltaThreshold,
        int foregroundLumaThreshold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sampleTimes);
        if (sampleTimes.Count < 2)
        {
            throw new ArgumentException("At least two sample times are required.", nameof(sampleTimes));
        }

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

            return AnalyzeFrames(
                frames,
                pixelDeltaThreshold,
                foregroundLumaThreshold);
        }
        finally
        {
            foreach ((_, Bitmap bitmap) in frames)
            {
                bitmap.Dispose();
            }
        }
    }

    internal static FrameDifferenceResponse AnalyzeFrames(
        IReadOnlyList<(TimeSpan Time, Bitmap Bitmap)> frames,
        int pixelDeltaThreshold,
        int foregroundLumaThreshold)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count < 2)
        {
            throw new ArgumentException("At least two rendered frames are required.", nameof(frames));
        }

        int normalizedDeltaThreshold = Math.Clamp(pixelDeltaThreshold, 1, 3 * byte.MaxValue);
        int normalizedForegroundThreshold = Math.Clamp(foregroundLumaThreshold, 0, byte.MaxValue);

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
        return new FrameDifferenceResponse(
            normalizedDeltaThreshold,
            normalizedForegroundThreshold,
            minimumRatio,
            averageRatio,
            samples,
            variations,
            coverage);
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

        // Both frames are read as sRGB bytes, as render_still reads its frame.
        StillRenderer.LumaPixelReader firstReader = StillRenderer.GetLumaPixelReader(previous.Bitmap);
        StillRenderer.LumaPixelReader secondReader = StillRenderer.GetLumaPixelReader(next.Bitmap);
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
                firstReader.Read(first, x * previous.Bitmap.BytesPerPixel, out byte r1, out byte g1, out byte b1);
                secondReader.Read(second, x * next.Bitmap.BytesPerPixel, out byte r2, out byte g2, out byte b2);
                int pixelDelta = Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);

                totalDelta += pixelDelta;
                if (pixelDelta > pixelDeltaThreshold)
                {
                    changedPixels++;
                }
            }
        }

        double changedRatio = totalPixels == 0 ? 0 : changedPixels / (double)totalPixels;
        double meanDelta = totalPixels == 0 ? 0 : totalDelta / (totalPixels * 3d * byte.MaxValue);

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
        StillRenderer.ForegroundCoverage coverage = StillRenderer.MeasureForeground(
            frame.Bitmap, (r, g, b) => Math.Max(r, Math.Max(g, b)) > foregroundLumaThreshold);

        return new MotionFrameCoverage(
            frame.Time.ToString("c"),
            coverage.ForegroundPixels,
            frame.Bitmap.Width * frame.Bitmap.Height,
            coverage.ForegroundPixelRatio,
            coverage.OccupiedBoundsRatio,
            coverage.MaxQuadrantForegroundRatio,
            coverage.Left,
            coverage.Top,
            coverage.Right,
            coverage.Bottom);
    }
}
