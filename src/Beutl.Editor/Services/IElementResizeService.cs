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
/// The pure media-offset shift (the Slip mode) is owned by <see cref="IElementSlipService"/>.
/// Slip is media-window-only; Roll / Slide are geometry-primary and additionally shift the
/// trimmed neighbour's media window, so media-offset writes are not exclusive to the slip
/// service — the shared primitives live in <c>SlippableMedia</c>. Each service owns its own
/// history commit boundary. These are in-tree editing seams consumed by the Timeline View;
/// the concrete implementations are wired in <c>EditViewModel.GetService</c>.
/// </para>
/// </summary>
public interface IElementResizeService
{
    void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests, bool ripple = false);

    /// <summary>
    /// Roll: shift the boundary between two adjacent clips, for every supplied pair at
    /// once. Per pair: <c>front.Length += d</c>, <c>back.Start += d</c>,
    /// <c>back.Length -= d</c>; total length is preserved. One shared delta — clamped to
    /// the intersection of every pair's window (see <see cref="GetTrimDeltaBounds"/>) — is
    /// applied to all pairs so grouped cuts (e.g. a video + audio pair on separate layers)
    /// move together. Each back clip's media offset is advanced by the same delta so its
    /// content stays anchored across the moving cut. Returns <see langword="false"/> (no
    /// commit) when <paramref name="pairs"/> is empty, any pair is invalid — front and back
    /// not distinct, on different layers, not both in <paramref name="scene"/>,
    /// <c>front.End != back.Start</c>, either side locked, or an element appearing in more
    /// than one pair — or the shared clamped delta is zero. A single invalid pair rejects
    /// the whole operation (never a partial, desynced roll).
    /// </summary>
    bool Roll(Scene scene, IReadOnlyList<ElementTrimPair> pairs, TimeSpan delta);

    /// <summary>
    /// Slide: shift a run of middle clips in time, for every supplied lane at once. Per
    /// lane: the front clip grows by the delta, every middle clip's Start shifts by it,
    /// and the back clip shrinks by it, preserving the total length. One shared delta —
    /// clamped to the intersection of every lane's front/back window (the middles' lengths
    /// are unaffected) — is applied to all lanes so a grouped block spanning layers moves
    /// together. Each back clip's media offset is advanced by the same delta so its content
    /// stays anchored; the middle clips only move in time. Returns <see langword="false"/>
    /// (no commit) when <paramref name="lanes"/> is empty, any lane is invalid — members on
    /// different layers, not all in <paramref name="scene"/>, the
    /// front → middles → back chain not contiguously adjacent, any participant locked, or
    /// an element appearing twice across lanes — or the shared clamped delta is zero. A
    /// single invalid lane rejects the whole operation (never a partial, desynced slide).
    /// </summary>
    bool Slide(Scene scene, IReadOnlyList<ElementSlideLane> lanes, TimeSpan delta);

    /// <summary>
    /// The delta window shared by <see cref="Roll"/> and <see cref="Slide"/>: the
    /// intersection over <paramref name="pairs"/> of each front/back window,
    /// <c>Min ≤ 0 ≤ Max</c>, bounded per pair by both clips keeping at least one frame at
    /// the scene's frame rate, the back in-point staying at or above zero, and — when the
    /// editor's ClampResizeToOriginalLength preference is on — the front out-point staying
    /// within its source. Both operations clamp with the same window on commit; the
    /// Timeline View queries it once at drag start so the per-pointer-frame preview cannot
    /// overshoot what the release will apply. <c>(Zero, Zero)</c> when
    /// <paramref name="pairs"/> is empty or no trim is possible. Adjacency is not validated
    /// here; callers check it before starting a drag.
    /// </summary>
    (TimeSpan Min, TimeSpan Max) GetTrimDeltaBounds(Scene scene, IReadOnlyList<ElementTrimPair> pairs);
}

public readonly record struct ElementResizeRequest(
    Element Element,
    TimeSpan NewStart,
    TimeSpan NewLength,
    int ZIndex);

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
