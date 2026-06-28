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

public sealed record MotionVariationResponse(
    bool PassesMinimumMotion,
    string Verdict,
    double MinimumChangedPixelRatio,
    double AverageChangedPixelRatio,
    IReadOnlyList<MotionVariationSample> Samples,
    IReadOnlyList<MotionVariationPair> PairVariations,
    IReadOnlyList<string> ReviewNotes);

public sealed class MotionVariationAnalyzer(StillRenderer stillRenderer)
{
    public async ValueTask<MotionVariationResponse> AnalyzeAsync(
        Scene scene,
        IReadOnlyList<TimeSpan> sampleTimes,
        float renderScale,
        double minChangedPixelRatio,
        int pixelDeltaThreshold,
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

            double minimumRatio = variations.Min(item => item.ChangedPixelRatio);
            double averageRatio = variations.Average(item => item.ChangedPixelRatio);
            bool passes = minimumRatio >= normalizedMinimumRatio;
            string verdict = passes
                ? "motion-variation-ok"
                : "low-motion-variation";

            List<string> notes = passes
                ? ["Temporal variation meets the requested changed-pixel threshold."]
                : [
                    "Temporal variation is low between at least one adjacent sample.",
                    "Revise the edit with stronger phase changes, more animated properties, or denser foreground/background motion."
                ];

            return new MotionVariationResponse(
                passes,
                verdict,
                minimumRatio,
                averageRatio,
                samples,
                variations,
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
}
