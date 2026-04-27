using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

public readonly record struct SnapResult(TimeSpan Time, TimeSpan? Snapped)
{
    public bool DidSnap => Snapped.HasValue;
}

public static class SnapHelper
{
    public const double DefaultThresholdPixel = 8;

    public static SnapResult Snap(
        TimeSpan time,
        IEnumerable<TimeSpan> candidates,
        float scale,
        double thresholdPixel = DefaultThresholdPixel)
    {
        TimeSpan threshold = thresholdPixel.PixelToTimeSpan(scale);
        TimeSpan bestDelta = TimeSpan.MaxValue;
        TimeSpan? best = null;

        foreach (TimeSpan candidate in candidates)
        {
            TimeSpan delta = (candidate - time).Duration();
            if (delta <= threshold && delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        return best.HasValue ? new SnapResult(best.Value, best.Value) : new SnapResult(time, null);
    }

    public static IEnumerable<TimeSpan> CollectElementCandidates(
        IEnumerable<Element> elements,
        Element? exclude,
        int? sameZIndex = null)
    {
        foreach (Element item in elements)
        {
            if (item == exclude) continue;
            if (sameZIndex is { } z && item.ZIndex != z) continue;

            yield return item.Start;
            yield return item.Start + item.Length;
        }
    }

    public static IEnumerable<TimeSpan> CollectSceneCandidates(Scene scene, TimeSpan currentTime)
    {
        yield return scene.Start;
        yield return scene.Start + scene.Duration;
        yield return currentTime;
        if (currentTime != TimeSpan.Zero) yield return TimeSpan.Zero;
    }
}
