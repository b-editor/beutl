using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Slip: shift the source-media window inside an <see cref="Element"/> without
/// changing the element's <see cref="Element.Start"/> / <see cref="Element.Length"/>.
/// Writes the media's in-source offset (<see cref="Beutl.Graphics.SourceVideo.OffsetPosition"/>
/// / <see cref="Beutl.Audio.Sound.OffsetPosition"/>), not element geometry, and owns
/// the single history commit boundary for one user-visible slip. Distinct from
/// <see cref="IElementResizeService"/> so a plugin replacing the geometry-trim family
/// does not inherit the media-offset family.
/// </summary>
public interface IElementSlipService
{
    /// <summary>
    /// Shift the source-media window inside <paramref name="element"/> by
    /// <paramref name="delta"/>. Adjusts <see cref="Beutl.Graphics.SourceVideo.OffsetPosition"/>
    /// and <see cref="Beutl.Audio.Sound.OffsetPosition"/> on the media objects inside
    /// <see cref="Element.Objects"/>. Returns <see langword="false"/> (no commit) when
    /// <paramref name="delta"/> is zero or the element carries no slip-able media.
    /// </summary>
    bool Slip(Element element, TimeSpan delta);
}
