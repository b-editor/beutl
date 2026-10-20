using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Structural commands that mutate the scene graph (<c>Scene.Children</c> /
/// <c>Scene.Groups</c>). Distinct from <see cref="IElementAttributeService"/>:
/// these use a per-call history command name. Each method commits exactly one entry.
/// </summary>
public interface IElementStructureService
{
    void Delete(Scene scene, IReadOnlyList<Element> elements, bool ripple = false);

    void Split(Scene scene, IReadOnlyList<Element> targets, TimeSpan at);

    void Group(Scene scene, IReadOnlyCollection<Guid> ids);

    void Ungroup(Scene scene, IReadOnlyCollection<Guid> ids);
}
