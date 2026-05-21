namespace Beutl.Editor.Services;

/// <summary>
/// Avalonia-free abstraction over the platform clipboard. The concrete
/// implementation lives in <c>Beutl.Editor.Components</c> and is wired in via
/// <c>IEditorContext.GetService</c>. Services in <c>Beutl.Editor</c> depend on
/// this interface instead of <c>Avalonia.Input.Platform.IClipboard</c> so that
/// they remain unit-testable without an Avalonia application.
/// </summary>
public interface IClipboardGateway
{
    Task<IReadOnlyList<string>> GetFormatsAsync();

    Task<string?> TryGetStringAsync(string format);

    Task<IReadOnlyList<string>?> TryGetFilePathsAsync();

    Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync();

    /// <summary>
    /// Publishes <paramref name="entries"/> to the platform clipboard.
    /// Returns <see langword="true"/> when the entries were committed,
    /// <see langword="false"/> when the platform clipboard is unavailable
    /// (e.g. no top-level <c>Avalonia</c> window). Destructive callers
    /// (Cut / Move-to-clipboard) MUST check the return and abort the
    /// destructive half when this is <see langword="false"/>, otherwise
    /// the user loses data they cannot paste back.
    /// </summary>
    Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries);

    Task ClearAsync();
}

public readonly record struct ClipboardEntry(string Format, string? Text, ReadOnlyMemory<byte>? Bytes);
