using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Applies resize requests to one or more <see cref="Element"/>s and commits
/// a single MoveElement history entry. The View handles the per-pointer-frame
/// preview by writing the VM reactive properties; the service is invoked once
/// on drag release with the final (start, length, zIndex) per element.
/// </summary>
public interface IElementResizeService
{
    void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests);
}

public readonly record struct ElementResizeRequest(
    Element Element,
    TimeSpan NewStart,
    TimeSpan NewLength,
    int ZIndex);
