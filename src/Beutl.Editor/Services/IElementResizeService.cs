using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Resizes the left/right edges of one or more <see cref="Element"/>s. The
/// <see cref="IElementResizeDragSession"/> owns the history commit. When
/// <see cref="BeginResize"/> is called with <c>clampToOriginalDuration</c>
/// the session refuses to extend an element past its source media length.
/// </summary>
public interface IElementResizeService
{
    IElementResizeDragSession BeginResize(
        Scene scene,
        IReadOnlyList<Element> elements,
        ResizeEdge edge,
        bool clampToOriginalDuration);
}

public interface IElementResizeDragSession : IDisposable
{
    void Commit(IReadOnlyList<ElementResizeRequest> finalSizes);

    void Cancel();
}

public enum ResizeEdge
{
    Left,
    Right,
}

public readonly record struct ElementResizeRequest(
    Element Element,
    TimeSpan NewStart,
    TimeSpan NewLength,
    int ZIndex);
