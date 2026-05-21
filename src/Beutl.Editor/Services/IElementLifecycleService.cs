using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Lifecycle commands invoked from element context menus: enable toggle,
/// remove from scene, delete from disk, split at a timestamp, group / ungroup.
/// Each operation commits a single history entry, removing the per-call
/// boilerplate that used to be scattered across <c>ElementViewModel</c>.
/// </summary>
public interface IElementLifecycleService
{
    void Exclude(Scene scene, IReadOnlyList<Element> elements);

    void Delete(Scene scene, IReadOnlyList<Element> elements);

    void SetEnabled(Element element, bool isEnabled);

    void SetAccentColor(Element element, Color color);

    SplitOutcome Split(Scene scene, IReadOnlyList<Element> targets, TimeSpan at);

    GroupOutcome Group(Scene scene, IReadOnlyCollection<Guid> ids);

    void Ungroup(Scene scene, IReadOnlyCollection<Guid> ids);
}

public sealed record SplitOutcome(IReadOnlyList<Element> NewElements)
{
    public static readonly SplitOutcome Empty = new([]);
}

public sealed record GroupOutcome(bool Created)
{
    public static readonly GroupOutcome NotCreated = new(false);
}
