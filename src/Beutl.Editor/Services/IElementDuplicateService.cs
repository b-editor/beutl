using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Selection-based duplicate and Alt+drag duplicate. Internally delegates to
/// the stateless <see cref="DuplicateHelper"/> for the actual file staging
/// and group remap, then commits a history entry. Centralizing the commit
/// here removes the duplicate "Regenerate -&gt; PlaceDuplicates -&gt; Commit"
/// boilerplate that used to live on <c>TimelineTabViewModel</c>.
/// </summary>
public interface IElementDuplicateService
{
    DuplicateOutcome DuplicateAtClickedPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        TimeSpan clickedFrame,
        int clickedLayer);

    bool DuplicateAtPosition(
        Scene scene,
        IReadOnlyList<Element> sources,
        TimeSpan anchorStart,
        int anchorZIndex);

    bool WouldOverlap(IReadOnlyList<Element> sources, TimeSpan anchorStart, int anchorZIndex);
}

public sealed record DuplicateOutcome(bool Success, TimeRange ScrollToRange, int ScrollToZIndex)
{
    public static readonly DuplicateOutcome Failed = new(false, default, 0);
}
