using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Element-geometry trim operations on <see cref="Element"/>s: writes to
/// <see cref="Element.Start"/> / <see cref="Element.Length"/> / <see cref="Element.ZIndex"/>
/// only. Each method owns a single history commit boundary for one user-visible
/// operation. The View handles per-pointer-frame preview via VM reactive properties;
/// the service runs once on drag release with the final values.
/// <para>
/// <see cref="Resize"/> writes the final (start, length, zIndex) per element in one
/// transaction. <see cref="Roll"/> and <see cref="Slide"/> are the roll / slide trim
/// modes, which adjust clip boundaries while preserving the total timeline length.
/// Media-offset shifts (the Slip mode) are owned by <see cref="IElementSlipService"/>
/// — they write the source-media window, not element geometry, and are kept separate
/// so a plugin replacing one family does not inherit the other.
/// </para>
/// </summary>
public interface IElementResizeService
{
    void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests, bool ripple = false);

    /// <summary>
    /// Roll: shift the boundary between two adjacent clips. <c>front.Length += d</c>,
    /// <c>back.Start += d</c>, <c>back.Length -= d</c>; total length is preserved. The
    /// delta is clamped so both clips keep at least one frame at the scene's frame rate,
    /// and — for source-backed clips — so the front's out-point stays within its source
    /// and the back's in-point stays within its source. The back clip's media offset is
    /// advanced by the same delta so its content stays anchored across the moving cut.
    /// Returns <see langword="false"/> (no commit) when <c>front.End != back.Start</c>
    /// (not adjacent) or the clamped delta is zero.
    /// </summary>
    bool Roll(Scene scene, Element front, Element back, TimeSpan delta);

    /// <summary>
    /// Slide: shift a middle clip's Start by <paramref name="delta"/>; the front clip
    /// grows by <paramref name="delta"/> and the back clip shrinks by
    /// <paramref name="delta"/>, preserving the total length. The delta is clamped so
    /// the front and back clips keep at least one frame at the scene's frame rate, and —
    /// for source-backed clips — so neither the front's out-point nor the back's in-point
    /// runs past its source. The back clip's media offset is advanced by the same delta so
    /// its content stays anchored; the middle clip only moves in time.
    /// Returns <see langword="false"/> (no commit) when the three clips are not
    /// mutually adjacent (<c>front.End != middle.Start</c> or
    /// <c>middle.End != back.Start</c>) or the clamped delta is zero.
    /// </summary>
    bool Slide(Scene scene, Element front, Element middle, Element back, TimeSpan delta);
}

public readonly record struct ElementResizeRequest(
    Element Element,
    TimeSpan NewStart,
    TimeSpan NewLength,
    int ZIndex);
