using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

public readonly record struct SnapResult(TimeSpan Time, bool DidSnap);

public static class SnapHelper
{
    private const double ThresholdPixel = 8;

    public static SnapResult Snap(TimeSpan time, IEnumerable<TimeSpan> candidates, float scale)
    {
        TimeSpan threshold = ThresholdPixel.PixelToTimeSpan(scale);
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

        return best.HasValue ? new SnapResult(best.Value, true) : new SnapResult(time, false);
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
