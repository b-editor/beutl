namespace Beutl.Engine;

/// <summary>A source that plays a timeline of its own, such as a media file or a referenced scene.</summary>
/// <remarks>
/// At the clip's local time t the source plays its timeline at <see cref="OffsetPosition"/> plus the integral of
/// <see cref="Speed"/> over [0, t]. Split, Trim and Slip keep that playback continuous by rewriting
/// <see cref="OffsetPosition"/> through this contract, so implementing it is all a source type needs for them.
/// Only <see cref="OffsetPosition"/> and <see cref="Speed"/> are required; the other members refine what edits may
/// do. An object that re-times another one, such as a time controller, is not a source.
/// </remarks>
public interface ITimeMappedSource : ISplittable
{
    /// <summary>The position on the timeline that plays at the start of the clip.</summary>
    IExpressionProperty<MediaTime> OffsetPosition { get; }

    /// <summary>The playback rate in percent, or <see langword="null"/> for a source that always plays at 100%.</summary>
    IExpressionProperty<float>? Speed { get; }

    /// <summary>
    /// Whether an expression or keyframe animation can change how local time maps onto the timeline, which the
    /// stored values do not show. No edit can follow such a mapping, so a split shifts <see cref="OffsetPosition"/>
    /// by the local time alone.
    /// </summary>
    /// <remarks><see cref="Speed"/> does not count; editors read its keyframes, and a split reads an expression on it at the cut.</remarks>
    bool HasDynamicMapping => false;

    /// <summary>
    /// Whether edits that the timeline's length bounds, such as Slip and Trim, must leave
    /// <see cref="OffsetPosition"/> alone because an expression picks the timeline. A split needs no length, so it
    /// still follows the mapping.
    /// </summary>
    bool HasDynamicTimeline => false;

    /// <summary>The length of the timeline, or <see langword="null"/> when it is unknown, as for offline media.</summary>
    /// <remarks>Edits keep <see cref="OffsetPosition"/> within it, and may reuse it for a copy of this source.</remarks>
    MediaTime? GetSourceDuration() => null;

    /// <summary>Moves the back piece of a split <paramref name="startDelta"/> further into the timeline.</summary>
    /// <remarks>Editors pass how far the back piece must advance to continue where the front piece stopped.</remarks>
    void ISplittable.NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
    {
        if (backward) OffsetPosition.Value += startDelta;
    }
}
