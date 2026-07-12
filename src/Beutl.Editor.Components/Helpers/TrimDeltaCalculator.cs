namespace Beutl.Editor.Components.Helpers;

public static class TrimDeltaCalculator
{
    /// <summary>
    /// The committed delta for a Slip / Roll / Slide drag: the press and release points are
    /// snapped with the <em>same</em> <paramref name="snap"/> function, then subtracted. Snapping
    /// both endpoints identically is what makes a no-move click near a cut resolve to a zero delta —
    /// otherwise only the release endpoint snaps and the operation commits a spurious one-frame trim.
    /// </summary>
    public static TimeSpan SnappedDelta(TimeSpan pressTime, TimeSpan releaseTime, Func<TimeSpan, TimeSpan> snap)
    {
        ArgumentNullException.ThrowIfNull(snap);
        return snap(releaseTime) - snap(pressTime);
    }

    /// <summary>
    /// Clamp a trim delta into the <c>[min, max]</c> window the resize service reported at
    /// drag start, so the per-pointer-frame preview never overshoots what the release commit
    /// will apply. Throws when <paramref name="min"/> exceeds <paramref name="max"/> — the
    /// service guarantees <c>Min ≤ 0 ≤ Max</c>, so an inverted window is a caller bug.
    /// </summary>
    public static TimeSpan ClampDelta(TimeSpan delta, TimeSpan min, TimeSpan max)
    {
        return TimeSpan.FromTicks(Math.Clamp(delta.Ticks, min.Ticks, max.Ticks));
    }
}
