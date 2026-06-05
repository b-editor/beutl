using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Structural commands that mutate the scene graph (`Scene.Children` and
/// `Scene.Groups`). Distinct from <see cref="IElementAttributeService"/>:
/// these operations frequently touch the file-system (regenerate, store
/// to URI) and produce different history command names per call. Each
/// method commits exactly one history entry.
/// </summary>
public interface IElementStructureService
{
    void Exclude(Scene scene, IReadOnlyList<Element> elements);

    void Delete(Scene scene, IReadOnlyList<Element> elements);

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
