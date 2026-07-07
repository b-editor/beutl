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
}
