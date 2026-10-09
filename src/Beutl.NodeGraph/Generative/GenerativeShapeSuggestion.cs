using System.Globalization;
using Beutl.Media;

namespace Beutl.NodeGraph.Generative;

/// <summary>
/// Picks the offered shape of a generation that best fits what it is made for: the
/// aspect ratio and resolution nearest the scene's frame, and the duration that covers a
/// stretch of the timeline. The AI tab, the nodes and the timeline ask the same question.
/// </summary>
public static class GenerativeShapeSuggestion
{
    /// <summary>The ratio nearest the frame's, or <paramref name="fallback"/> when the frame has no size.</summary>
    public static string NearestAspectRatio(
        IReadOnlyList<string> ratios,
        PixelSize? frameSize,
        string fallback)
    {
        ArgumentNullException.ThrowIfNull(ratios);
        if (ratios.Count == 0)
            throw new ArgumentException("At least one aspect ratio is required.", nameof(ratios));

        if (frameSize is not { Width: > 0, Height: > 0 } size)
            return ratios.Contains(fallback) ? fallback : ratios[0];

        double target = (double)size.Width / size.Height;
        string? nearest = null;
        double nearestDistance = double.PositiveInfinity;
        foreach (string ratio in ratios)
        {
            if (!TryParseAspectRatio(ratio, out double candidate))
                continue;
            // Compared in log space so 16:9 and 9:16 sit an equal distance from
            // square; a linear difference would always favour the wider one.
            double distance = Math.Abs(Math.Log(target) - Math.Log(candidate));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = ratio;
            }
        }

        return nearest ?? (ratios.Contains(fallback) ? fallback : ratios[0]);
    }

    /// <summary>
    /// The smallest resolution that still covers the frame's short side, so the clip is not
    /// upscaled in the scene; the largest one when none does. Labels the service does not
    /// describe by height are passed over.
    /// </summary>
    public static string SuggestResolution(IReadOnlyList<string> resolutions, PixelSize? frameSize)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        if (resolutions.Count == 0)
            throw new ArgumentException("At least one resolution is required.", nameof(resolutions));

        int needed = frameSize is { Width: > 0, Height: > 0 } size ? Math.Min(size.Width, size.Height) : 1080;
        string? smallestCovering = null;
        int smallestCoveringLines = int.MaxValue;
        string? largest = null;
        int largestLines = 0;
        foreach (string resolution in resolutions)
        {
            if (LinesOf(resolution) is not { } lines)
                continue;
            if (lines >= needed && lines < smallestCoveringLines)
            {
                smallestCovering = resolution;
                smallestCoveringLines = lines;
            }

            if (lines > largestLines)
            {
                largest = resolution;
                largestLines = lines;
            }
        }

        return smallestCovering ?? largest ?? resolutions[0];
    }

    /// <summary>
    /// The duration to ask for. To fill <paramref name="span"/>, the shortest one that covers
    /// it, or the longest when none does; otherwise <paramref name="preferredSeconds"/> when
    /// offered, or the first offered.
    /// </summary>
    public static int SuggestDuration(IReadOnlyList<int> durations, TimeSpan? span, int preferredSeconds)
    {
        ArgumentNullException.ThrowIfNull(durations);
        if (durations.Count == 0)
            throw new ArgumentException("At least one duration is required.", nameof(durations));

        if (span is { } length && length > TimeSpan.Zero)
        {
            int[] covering = durations.Where(seconds => TimeSpan.FromSeconds(seconds) >= length).ToArray();
            return covering.Length != 0 ? covering.Min() : durations.Max();
        }

        return durations.Contains(preferredSeconds) ? preferredSeconds : durations[0];
    }

    /// <summary>The number of lines a resolution label such as 720p, fhd or 4k stands for.</summary>
    public static int? LinesOf(string resolution)
    {
        string label = resolution.Trim().ToLowerInvariant();
        switch (label)
        {
            case "hd":
                return 720;
            case "fhd":
                return 1080;
            case "2k":
                return 1440;
            case "4k":
                return 2160;
        }

        if (label.EndsWith('p')
            && int.TryParse(label.AsSpan(0, label.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int lines)
            && lines > 0)
        {
            return lines;
        }

        return null;
    }

    public static bool TryParseAspectRatio(string value, out double ratio)
    {
        ratio = 0;
        string[] parts = value.Split(':');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        ratio = width / height;
        return true;
    }
}
