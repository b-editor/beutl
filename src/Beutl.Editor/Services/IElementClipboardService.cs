using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Cut / Copy / Paste pipeline for <see cref="Element"/>s. The service
/// consumes <see cref="IClipboardGateway"/> so it can be exercised in unit
/// tests without an Avalonia clipboard. <see cref="PasteAsync"/> dispatches
/// internally over the four supported payloads (element list JSON, single
/// element JSON, files, bitmap) and commits a single history entry.
/// </summary>
public interface IElementClipboardService
{
    Task CopyAsync(IReadOnlyList<Element> elements);

    Task<bool> CutAsync(Scene scene, IReadOnlyList<Element> elements);

    Task<ElementPasteOutcome> PasteAsync(Scene scene, TimeSpan clickedFrame, int clickedLayer);
}

public sealed record ElementPasteOutcome(
    bool Pasted,
    IReadOnlyList<Element> NewElements,
    TimeRange ScrollTo,
    int ScrollToZIndex)
{
    public static readonly ElementPasteOutcome Empty = new(false, [], default, 0);
}
