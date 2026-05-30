using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;

namespace Beutl.Editor.Components.Services;

/// <summary>
/// Avalonia-backed implementation of <see cref="IClipboardGateway"/>. Lives in
/// <c>Beutl.Editor.Components</c> so <c>Beutl.Editor</c> can stay Avalonia-free.
/// Maps Avalonia clipboard formats (<see cref="DataFormat.File"/>, <see cref="DataFormat.Bitmap"/>,
/// and the Beutl element JSON formats) onto the plain string identifiers in
/// <see cref="BeutlClipboardFormats"/>.
/// </summary>
public sealed class AvaloniaClipboardGateway : IClipboardGateway
{
    public async Task<IReadOnlyList<string>> GetFormatsAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard is null) return [];

        IReadOnlyList<DataFormat> formats = await clipboard.GetDataFormatsAsync();
        var result = new List<string>(formats.Count);
        foreach (DataFormat format in formats)
        {
            result.Add(MapToString(format));
        }

        return result;
    }

    public async Task<string?> TryGetStringAsync(string format)
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard is null) return null;

        DataFormat? avFormat = MapFromString(format);
        if (avFormat is null) return null;

        if (avFormat is DataFormat<string> stringFormat)
        {
            return await clipboard.TryGetValueAsync(stringFormat);
        }

        return null;
    }

    public async Task<IReadOnlyList<string>?> TryGetFilePathsAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard is null) return null;

        IReadOnlyList<IStorageItem>? files = await clipboard.TryGetFilesAsync();
        if (files is null) return null;

        var paths = new List<string>(files.Count);
        foreach (IStorageItem item in files)
        {
            if (item.TryGetLocalPath() is { } path) paths.Add(path);
        }

        return paths;
    }

    public async Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard is null) return null;

        var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null) return null;

        using var ms = new MemoryStream();
        bitmap.Save(ms);
        return ms.ToArray();
    }

    public async Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries)
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard is null) return false;

        using var data = new DataTransfer();
        foreach (ClipboardEntry entry in entries)
        {
            if (entry.Text is null) continue;

            if (entry.Format == BeutlClipboardFormats.Text)
            {
                // Route plain-text payloads through the platform's native
                // text slot so other apps see them on a normal "Paste".
                data.Add(DataTransferItem.CreateText(entry.Text));
                continue;
            }

            DataFormat? format = MapFromString(entry.Format);
            if (format is DataFormat<string> stringFormat)
            {
                data.Add(DataTransferItem.Create(stringFormat, entry.Text));
            }
        }

        await clipboard.SetDataAsync(data);
        return true;
    }

    public async Task ClearAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard is null) return;
        await clipboard.ClearAsync();
    }

    private static string MapToString(DataFormat format)
    {
        if (format == DataFormat.File) return BeutlClipboardFormats.Files;
        if (format == DataFormat.Bitmap) return BeutlClipboardFormats.Bitmap;
        if (format == BeutlDataFormats.Element) return BeutlClipboardFormats.Element;
        if (format == BeutlDataFormats.Elements) return BeutlClipboardFormats.Elements;
        return format.ToString() ?? string.Empty;
    }

    private static DataFormat? MapFromString(string format)
    {
        return format switch
        {
            BeutlClipboardFormats.Element => BeutlDataFormats.Element,
            BeutlClipboardFormats.Elements => BeutlDataFormats.Elements,
            _ => null,
        };
    }
}
