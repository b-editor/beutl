using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Applies resize requests to one or more <see cref="Element"/>s and commits one
/// MoveElement entry. The View handles per-pointer-frame preview via VM reactive
/// properties; the service runs once on drag release with the final
/// (start, length, zIndex) per element.
/// </summary>
public interface IElementResizeService
{
    void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests, bool ripple = false);
}

public readonly record struct ElementResizeRequest(
    Element Element,
    TimeSpan NewStart,
    TimeSpan NewLength,
    int ZIndex);
