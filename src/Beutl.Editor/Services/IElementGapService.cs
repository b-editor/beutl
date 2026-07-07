using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Closes timeline gaps between <see cref="Element"/>s on a <see cref="Scene"/>, owning the
/// history commit. Commits one undo entry only when the scene actually changes.
/// </summary>
public interface IElementGapService
{
    /// <summary>Closes the gap immediately after <paramref name="anchor"/> on its ZIndex layer.
    /// Returns <see langword="false"/> (no commit) when there is no gap to close.</summary>
    bool CloseGapAfter(Scene scene, Element anchor);

    /// <summary>Closes every gap across all ZIndex layers. Returns the number of gaps closed;
    /// commits one undo entry only when that number is greater than zero.</summary>
    int CloseAllGaps(Scene scene);
}
