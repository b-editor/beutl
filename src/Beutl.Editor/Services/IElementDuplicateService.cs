using System.Collections.Immutable;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Selection-based and Alt+drag duplicate. File staging and group remap are in
/// <see cref="DuplicateHelper"/>; each call commits one history entry.
/// </summary>
public interface IElementDuplicateService
{
    DuplicateOutcome DuplicateAtClickedPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        MediaTime clickedFrame,
        int clickedLayer);

    /// <summary>
    /// Duplicates sources whose groups are not those of the scene, such as clips from the clipboard.
    /// </summary>
    /// <param name="sourceGroups">The groups of the sources, which take the place of the scene's.</param>
    DuplicateOutcome DuplicateAtClickedPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        MediaTime clickedFrame,
        int clickedLayer,
        IReadOnlyCollection<ImmutableHashSet<Guid>> sourceGroups)
    {
        return DuplicateAtClickedPosition(scene, sources, clickedFrame, clickedLayer);
    }

    bool DuplicateAtPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        MediaTime anchorStart,
        int anchorZIndex);

    bool WouldOverlap(IReadOnlyList<Element> sources, MediaTime anchorStart, int anchorZIndex);
}

public sealed record DuplicateOutcome(bool Success, TimeRange ScrollToRange, int ScrollToZIndex)
{
    public static readonly DuplicateOutcome Failed = new(false, default, 0);
}
