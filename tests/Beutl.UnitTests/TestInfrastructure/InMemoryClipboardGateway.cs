using Beutl.Editor.Services;

namespace Beutl.UnitTests.TestInfrastructure;

public sealed class InMemoryClipboardGateway : IClipboardGateway
{
    private readonly Dictionary<string, string> _entries = new();
    private IReadOnlyList<string>? _filePaths;
    private byte[]? _bitmapPng;

    public bool SimulateUnavailable { get; set; }

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
        => Task.FromResult(_entries.TryGetValue(format, out string? value) ? value : null);

    public Task<IReadOnlyList<string>?> TryGetFilePathsAsync() => Task.FromResult(_filePaths);

    public Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync()
        => Task.FromResult<ReadOnlyMemory<byte>?>(_bitmapPng is null ? null : _bitmapPng);

    public Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries)
    {
        if (SimulateUnavailable) return Task.FromResult(false);

        _entries.Clear();
        foreach (ClipboardEntry entry in entries)
        {
            if (entry.Text is not null) _entries[entry.Format] = entry.Text;
        }

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
