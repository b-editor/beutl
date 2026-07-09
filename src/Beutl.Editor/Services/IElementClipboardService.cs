using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Cut / Copy / Paste pipeline for <see cref="Element"/>s. Consumes
/// <see cref="IClipboardGateway"/> so it stays unit-testable without an Avalonia
/// clipboard. <see cref="PasteAsync"/> dispatches over the four supported payloads
/// (element-list JSON, single-element JSON, files, bitmap) and commits one entry.
/// </summary>
public interface IElementClipboardService
{
    /// <summary>
    /// Publishes <paramref name="elements"/> to the platform clipboard. Returns
    /// <see langword="false"/> when the clipboard is unavailable; callers that
    /// destroy the source on a successful copy (e.g. Cut) MUST check it.
    /// </summary>
    Task<bool> CopyAsync(IReadOnlyList<Element> elements);

    Task<bool> CutAsync(Scene scene, IReadOnlyList<Element> elements, bool ripple = false);

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
