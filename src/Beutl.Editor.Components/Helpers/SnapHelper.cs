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

    // A line of the BPM grid catches the time only where no other target is in reach. Lines closer together than
    // twice the reach leave no gap between them, and would otherwise take a drag from the edge or playhead it aims at.
    public static SnapResult Snap(
        MediaTime time, IEnumerable<MediaTime> candidates, BpmGridOptions grid, Rational rate, float scale)
    {
        SnapResult snap = Snap(time, candidates, scale);
        return snap.DidSnap ? snap : Snap(time, CollectGridCandidates(grid, time, rate, scale), scale);
    }

    public static IEnumerable<MediaTime> CollectElementCandidates(
        IEnumerable<Element> elements,
        Element? exclude)
    {
        foreach (Element item in elements)
        {
            if (item == exclude) continue;

            yield return item.Start.Value;
            yield return item.Start.Value + item.Length.Value;
        }
    }

    public static IEnumerable<MediaTime> CollectSceneCandidates(Scene scene, MediaTime currentTime)
    {
        yield return scene.Start.Value;
        yield return scene.Start.Value + scene.Duration.Value;
        yield return currentTime;
        if (currentTime != MediaTime.Zero) yield return MediaTime.Zero;
        foreach (SceneMarker marker in scene.Markers)
        {
            yield return marker.Time.Value;
        }
    }

    // The two lines of the BPM grid around the time, of those shown at this scale. Each is moved to the frame it falls
    // nearest, where a drag lands, since a beat need not fall on a frame.
    public static IEnumerable<MediaTime> CollectGridCandidates(
        BpmGridOptions grid, MediaTime time, Rational rate, float scale)
    {
        if (BpmGridLines.GetShownInterval(grid, scale) is not { } interval) yield break;

        double offset = grid.Offset.ToSeconds();
        double before = offset + Math.Floor((time.ToSeconds() - offset) / interval) * interval;
        // The timeline starts at 0, so no line before it shows.
        if (before >= 0) yield return OnFrame(before);
        if (before + interval >= 0) yield return OnFrame(before + interval);

        MediaTime OnFrame(double seconds) => MediaTime.FromSeconds(seconds).RoundToFrame(rate, MidpointRounding.AwayFromZero);
    }
}
