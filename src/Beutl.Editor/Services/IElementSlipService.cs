using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Slip: shift the source-media window inside one or more <see cref="Element"/>s without
/// changing any element's <see cref="Element.Start"/> / <see cref="Element.Length"/>.
/// Writes the media's in-source offset (<see cref="Beutl.Graphics.SourceVideo.OffsetPosition"/>
/// / <see cref="Beutl.Audio.Sound.OffsetPosition"/>), not element geometry, and owns
/// the single history commit boundary for one user-visible slip. Kept separate from
/// <see cref="IElementResizeService"/> because Slip is media-window-only; Roll / Slide
/// over there are geometry-primary but also shift the trimmed neighbour's media window
/// through the same <c>SlippableMedia</c> primitives, so media-offset writes are not
/// exclusive to this service.
/// </summary>
public interface IElementSlipService
{
    /// <summary>
    /// Shift the source-media window inside every element of <paramref name="elements"/> by
    /// <paramref name="delta"/>. Adjusts <see cref="Beutl.Graphics.SourceVideo.OffsetPosition"/>
    /// and <see cref="Beutl.Audio.Sound.OffsetPosition"/> on every slip-able media object
    /// reachable from <see cref="Element.Objects"/>, including sources nested inside
    /// Drawable and Sound containers. A single effective delta — the largest the tightest
    /// stream across all supplied elements can accept without leaving its source — is applied
    /// to every stream so linked media (e.g. a video + audio pair, whether inside one element
    /// or grouped across elements) stay in sync. Elements that are duplicated in the list,
    /// no longer in <paramref name="scene"/>, locked (directly or via their layer), or carry
    /// no slip-able media are dropped at this mutation boundary (matching
    /// <see cref="IElementResizeService.Resize"/>) rather than blocking the group. Returns
    /// <see langword="false"/> (no commit) when <paramref name="delta"/> is zero, no element
    /// survives that filter, or the shared clamped delta is zero.
    /// </summary>
    bool Slip(Scene scene, IReadOnlyList<Element> elements, TimeSpan delta);
}
