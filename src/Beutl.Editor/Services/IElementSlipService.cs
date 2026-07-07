using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Slip: shift the source-media window inside an <see cref="Element"/> without
/// changing the element's <see cref="Element.Start"/> / <see cref="Element.Length"/>.
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
    /// Shift the source-media window inside <paramref name="element"/> by
    /// <paramref name="delta"/>. Adjusts <see cref="Beutl.Graphics.SourceVideo.OffsetPosition"/>
    /// and <see cref="Beutl.Audio.Sound.OffsetPosition"/> on every slip-able media object
    /// reachable from <see cref="Element.Objects"/>, including sources nested inside
    /// Drawable and Sound containers. A single effective delta — the largest the tightest
    /// stream can accept without leaving its source — is applied to all streams so linked
    /// media (e.g. a video + audio pair) stay in sync. Returns <see langword="false"/>
    /// (no commit) when <paramref name="delta"/> is zero, the element carries no slip-able
    /// media, or the shared clamped delta is zero.
    /// </summary>
    bool Slip(Element element, TimeSpan delta);
}
