namespace Beutl.Editor.Services;

/// <summary>
/// Avalonia-free abstraction over the platform clipboard (concrete impl in
/// <c>Beutl.Editor.Components</c>, wired via <c>IEditorContext.GetService</c>).
/// Services depend on this rather than <c>Avalonia.Input.Platform.IClipboard</c>
/// so they stay unit-testable without an Avalonia application.
/// </summary>
public interface IClipboardGateway
{
    Task<IReadOnlyList<string>> GetFormatsAsync();

    Task<string?> TryGetStringAsync(string format);

    Task<IReadOnlyList<string>?> TryGetFilePathsAsync();

    Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync();

    /// <summary>
    /// Publishes <paramref name="entries"/> to the platform clipboard. Returns
    /// <see langword="false"/> when the clipboard is unavailable (e.g. no top-level
    /// window). Destructive callers (Cut / Move-to-clipboard) MUST abort their
    /// destructive half on <see langword="false"/>, or the user loses data they
    /// cannot paste back.
    /// </summary>
    Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries);

    Task ClearAsync();
}

public readonly record struct ClipboardEntry(string Format, string? Text, ReadOnlyMemory<byte>? Bytes);
