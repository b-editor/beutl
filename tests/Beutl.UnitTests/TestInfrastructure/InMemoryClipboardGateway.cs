using Beutl.Editor.Services;

namespace Beutl.UnitTests.TestInfrastructure;

public sealed class InMemoryClipboardGateway : IClipboardGateway
{
    private readonly Dictionary<string, string> _entries = new();
    private IReadOnlyList<string>? _filePaths;
    private byte[]? _bitmapPng;

    public bool SimulateUnavailable { get; set; }

    // Hooks that run inside the awaited operation, letting a test mutate lock state mid-await to
    // exercise the time-of-check-to-time-of-use guards in cut / paste.
    public Action? OnSetAsync { get; set; }

    public Action? OnTryGetString { get; set; }

    public Action? OnTryGetBitmap { get; set; }

    public void SetSingle(string format, string content) => _entries[format] = content;

    public void SetFiles(IReadOnlyList<string> files) => _filePaths = files;

    public void SetBitmap(byte[] png) => _bitmapPng = png;

    public Task<IReadOnlyList<string>> GetFormatsAsync()
    {
        var formats = new List<string>(_entries.Keys);
        if (_filePaths is not null) formats.Add(BeutlClipboardFormats.Files);
        if (_bitmapPng is not null) formats.Add(BeutlClipboardFormats.Bitmap);
        return Task.FromResult<IReadOnlyList<string>>(formats);
    }

    public Task<string?> TryGetStringAsync(string format)
    {
        string? value = _entries.TryGetValue(format, out string? v) ? v : null;
        OnTryGetString?.Invoke();
        return Task.FromResult(value);
    }

    public Task<IReadOnlyList<string>?> TryGetFilePathsAsync() => Task.FromResult(_filePaths);

    public Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync()
    {
        ReadOnlyMemory<byte>? png = _bitmapPng is null ? null : _bitmapPng;
        OnTryGetBitmap?.Invoke();
        return Task.FromResult(png);
    }

    public Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries)
    {
        if (SimulateUnavailable) return Task.FromResult(false);

        _entries.Clear();
        foreach (ClipboardEntry entry in entries)
        {
            if (entry.Text is not null) _entries[entry.Format] = entry.Text;
        }

        OnSetAsync?.Invoke();
        return Task.FromResult(true);
    }

    public Task ClearAsync()
    {
        _entries.Clear();
        _filePaths = null;
        _bitmapPng = null;
        return Task.CompletedTask;
    }
}
