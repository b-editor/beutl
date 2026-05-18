namespace Beutl.Animation;

/// <summary>
/// Helper for writing back keyframe values during a drag. By updating the surrounding (previous/next)
/// keyframes to "snapshot value at OnPressed + drag delta", both keyframes shift in the same direction
/// so the animation amplitude is preserved while a global offset is applied.
///
/// <para>
/// The drag delta must be the "total delta from the start of the drag to now". Because it is added
/// directly to the snapshot, OnMoved becomes idempotent (the same delta produces the same result).
/// </para>
/// </summary>
internal static class KeyFrameDeltaHelper
{
    /// <summary>
    /// Returns false when both surrounding keyframes are null (= no keyframe animation, the caller should
    /// fall back to writing CurrentValue directly).
    /// </summary>
    public static bool ApplyDelta<T>(
        KeyFrame<T>? previous,
        KeyFrame<T>? next,
        T startPrev,
        T startNext,
        T delta)
        where T : notnull, System.Numerics.IAdditionOperators<T, T, T>
    {
        if (previous == null && next == null) return false;
        if (previous != null) previous.Value = startPrev + delta;
        if (next != null) next.Value = startNext + delta;
        return true;
    }

    /// <summary>
    /// Snapshots and returns the surrounding keyframes' Values. The null side is filled with <paramref name="fallback"/>.
    /// </summary>
    public static (T prev, T next) CaptureStartValues<T>(
        KeyFrame<T>? previous,
        KeyFrame<T>? next,
        T fallback)
        where T : notnull
    {
        // KeyFrame<T>.Value is T? for nullability; current callers pin T to struct types so `!` is safe.
        T prev = previous != null ? previous.Value! : fallback;
        T nextVal = next != null ? next.Value! : fallback;
        return (prev, nextVal);
    }
}
