using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

public readonly record struct SnapResult(MediaTime Time, bool DidSnap);

public static class SnapHelper
{
    private const double ThresholdPixel = 8;

    public static SnapResult Snap(MediaTime time, IEnumerable<MediaTime> candidates, float scale)
    {
        MediaTime threshold = ThresholdPixel.PixelToTime(scale);
        MediaTime bestDelta = MediaTime.MaxValue;
        MediaTime? best = null;

        foreach (MediaTime candidate in candidates)
        {
            MediaTime delta = candidate >= time ? candidate - time : time - candidate;
            if (delta <= threshold && delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        return best.HasValue ? new SnapResult(best.Value, true) : new SnapResult(time, false);
    }

    public static IEnumerable<MediaTime> CollectElementCandidates(
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

    public static IEnumerable<MediaTime> CollectSceneCandidates(Scene scene, MediaTime currentTime)
    {
        yield return scene.Start;
        yield return scene.Start + scene.Duration;
        yield return currentTime;
        if (currentTime != MediaTime.Zero) yield return MediaTime.Zero;
    }
}
