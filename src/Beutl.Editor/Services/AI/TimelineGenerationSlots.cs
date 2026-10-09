using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.AI;

/// <summary>A stretch of one layer that a generation will fill.</summary>
public readonly record struct TimelineGenerationSlot(int Layer, TimeRange Range);

/// <summary>Finds room on the timeline for what a generation makes.</summary>
public static class TimelineGenerationSlots
{
    /// <summary>The highest layer a result is placed on; past it the timeline has no room.</summary>
    public const int MaxLayer = 1000;

    /// <summary>
    /// Whether <paramref name="range"/> on <paramref name="layer"/> holds no element and no
    /// other generation, and the layer is not locked. <paramref name="ignore"/> is an element
    /// that is about to be replaced, so its own stretch counts as free.
    /// </summary>
    public static bool IsFree(
        Scene scene,
        int layer,
        TimeRange range,
        IEnumerable<TimelineGenerationSlot>? reserved = null,
        Element? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (layer < 0 || range.Duration <= TimeSpan.Zero || scene.IsLayerLocked(layer))
            return false;

        foreach (Element element in scene.Children)
        {
            if (ReferenceEquals(element, ignore) || element.ZIndex != layer)
                continue;
            if (element.Range.Intersects(range))
                return false;
        }

        if (reserved is not null)
        {
            foreach (TimelineGenerationSlot slot in reserved)
            {
                if (slot.Layer == layer && slot.Range.Intersects(range))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The first layer from <paramref name="preferredLayer"/> upward where
    /// <paramref name="span"/> consecutive layers are free over <paramref name="range"/>,
    /// or null when there is none within <see cref="MaxLayer"/>.
    /// </summary>
    public static int? FindFreeLayer(
        Scene scene,
        TimeRange range,
        int preferredLayer,
        int span = 1,
        IEnumerable<TimelineGenerationSlot>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentOutOfRangeException.ThrowIfLessThan(span, 1);
        TimelineGenerationSlot[] slots = reserved?.ToArray() ?? [];
        for (int layer = Math.Max(0, preferredLayer); layer + span - 1 <= MaxLayer; layer++)
        {
            bool free = true;
            for (int offset = 0; offset < span && free; offset++)
                free = IsFree(scene, layer + offset, range, slots);
            if (free)
                return layer;
        }

        return null;
    }

    /// <summary>
    /// The gap on <paramref name="layer"/> at <paramref name="time"/>: its start, how long it may
    /// run (null when nothing follows), and the element it follows, or null when the time is
    /// covered by an element or a generation, or the layer is locked.
    /// </summary>
    public static TimelineGap? FindGap(
        Scene scene,
        int layer,
        TimeSpan time,
        IEnumerable<TimelineGenerationSlot>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (layer < 0 || time < TimeSpan.Zero || scene.IsLayerLocked(layer))
            return null;

        Element? previous = null;
        TimeSpan? nextStart = null;
        foreach (Element element in scene.Children)
        {
            if (element.ZIndex != layer)
                continue;
            if (element.Range.Contains(time))
                return null;
            if (element.Range.End <= time && (previous is null || element.Range.End > previous.Range.End))
                previous = element;
            if (element.Start > time && (nextStart is null || element.Start < nextStart))
                nextStart = element.Start;
        }

        TimeSpan start = previous?.Range.End ?? time;
        TimeSpan? end = nextStart;
        if (reserved is not null)
        {
            foreach (TimelineGenerationSlot slot in reserved)
            {
                if (slot.Layer != layer)
                    continue;
                if (slot.Range.Contains(time))
                    return null;
                if (slot.Range.End <= time && slot.Range.End > start)
                {
                    start = slot.Range.End;
                    previous = null;
                }
                if (slot.Range.Start > time && (end is null || slot.Range.Start < end))
                    end = slot.Range.Start;
            }
        }

        return new TimelineGap(layer, start, end is { } e ? e - start : null, previous);
    }
}

/// <summary>An empty stretch of a layer: where it starts, how long it may run, and what it follows.</summary>
public sealed record TimelineGap(int Layer, TimeSpan Start, TimeSpan? MaxLength, Element? Previous);
