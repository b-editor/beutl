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
