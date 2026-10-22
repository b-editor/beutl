using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Element-geometry trim operations on <see cref="Element"/>s: primarily writes
/// <see cref="Element.Start"/> / <see cref="Element.Length"/> / <see cref="Element.ZIndex"/>.
/// Each method owns a single history commit boundary for one user-visible operation. The
/// View handles per-pointer-frame preview via VM reactive properties; the service runs once
/// on drag release with the final values.
/// <para>
/// <see cref="Resize"/> writes the final (start, length, zIndex) per element in one
/// transaction. <see cref="Roll"/> and <see cref="Slide"/> are the roll / slide trim
/// modes, which adjust clip boundaries while preserving the total timeline length; they
/// additionally advance the trimmed neighbour's media offset so its content stays anchored
/// across the moving cut (see the shared primitives in <c>SlippableMedia</c>).
/// </para>
/// <para>
/// The pure media-offset shift (Slip) is <see cref="IElementSlipService"/>, which shares the
/// <c>SlippableMedia</c> primitives with Roll and Slide. The Timeline View gets both services
/// from <c>EditViewModel.GetService</c>.
/// </para>
/// </summary>
public interface IElementResizeService
{
    void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests, bool ripple = false);

    /// <summary>
    /// Sets <paramref name="element"/>'s length to its media's original duration, floored to the
    /// scene's frame rate, keeping its start and layer. Without <paramref name="ripple"/>, the clip
    /// stops where the next clip on its layer starts. Returns <see langword="false"/> without a history
    /// entry when the element is locked or its media has no original duration.
    /// </summary>
    bool ResizeToOriginalDuration(Scene scene, Element element, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(element);
        if (scene.IsElementLocked(element)
            || SlippableMedia.GetOriginalDuration(element) is not { } originalDuration)
        {
            return false;
        }

        MediaTime duration = originalDuration.RoundToFrame(SceneTimeRangeService.GetFrameRate(scene), MidpointRounding.ToNegativeInfinity);
        if (!ripple && element.GetAfter(element.ZIndex, element.Range.End) is { } after)
        {
            MediaTime delta = after.Start - element.Start;
            if (delta < duration)
            {
                duration = delta;
            }
        }

        var request = new ElementResizeRequest(element, element.Start, duration, element.ZIndex) { ClampToSource = true };
        Resize(scene, [request], ripple);
        return true;
    }

    /// <summary>
    /// Roll: moves the cut of every front/back pair by one shared delta, clamped to every pair's window, keeping
    /// each back clip's content in place. Returns <see langword="false"/> without a commit when any pair is not
    /// adjacent on one layer of the scene, is locked or repeats an element, or when the delta clamps to zero.
    /// </summary>
    bool Roll(Scene scene, IReadOnlyList<ElementTrimPair> pairs, MediaTime delta);

    /// <summary>
    /// Slide: moves the middle clips of every lane by one shared delta, clamped to every lane, growing the front and
    /// shrinking the back with its content kept in place. Returns <see langword="false"/> without a commit when any
    /// lane is not a contiguous chain on one layer of the scene, has a locked or repeated element, or the delta
    /// clamps to zero.
    /// </summary>
    bool Slide(Scene scene, IReadOnlyList<ElementSlideLane> lanes, MediaTime delta);
}

public readonly record struct ElementResizeRequest(
    Element Element,
    MediaTime NewStart,
    MediaTime NewLength,
    int ZIndex)
{
    // Commands requesting an original source length must retain media limits
    // even when ordinary edge resizing is allowed to extend beyond the source.
    public bool ClampToSource { get; init; }
}

/// <summary>
/// One rolled cut: <see cref="Front"/> ends exactly where <see cref="Back"/> starts, on
/// the same layer.
/// </summary>
public readonly record struct ElementTrimPair(Element Front, Element Back);

/// <summary>
/// One slid lane: a contiguous run of <see cref="Middles"/> (ordered by Start) with the
/// adjacent <see cref="Front"/> before and <see cref="Back"/> after, all on the same layer.
/// </summary>
public readonly record struct ElementSlideLane(Element Front, IReadOnlyList<Element> Middles, Element Back);
