using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.Services;

/// <summary>
/// Avalonia-backed <see cref="IClipboardGateway"/>, kept here so <c>Beutl.Editor</c>
/// stays Avalonia-free. Maps Avalonia clipboard formats onto the plain string
/// identifiers in <see cref="BeutlClipboardFormats"/>.
/// </summary>
public sealed class AvaloniaClipboardGateway : IClipboardGateway
{
    private readonly IClipboard? _clipboard;
    private readonly DataFormat<string>? _stringFormat;

    public AvaloniaClipboardGateway()
    {
    }

    internal AvaloniaClipboardGateway(IClipboard clipboard, DataFormat<string>? stringFormat = null)
    {
        _clipboard = clipboard;
        _stringFormat = stringFormat;
    }

    internal static IClipboardGateway? Resolve(
        IServiceProvider services, IClipboard? clipboard = null, DataFormat<string>? stringFormat = null)
    {
        var gateway = services.GetService<IClipboardGateway>();
        if (gateway is not null and not AvaloniaClipboardGateway) return gateway;

        // Bind to the command's window, or the editor's default clipboard, for the whole operation.
        clipboard ??= (gateway as AvaloniaClipboardGateway)?._clipboard ?? ClipboardHelper.GetClipboard();
        return clipboard is null ? null : new AvaloniaClipboardGateway(clipboard, stringFormat);
    }

    internal static string GetStringFormatName(DataFormat<string> format)
        => format == DataFormat.Text ? BeutlClipboardFormats.Text : format.Identifier;

    private IClipboard? GetClipboard() => _clipboard ?? ClipboardHelper.GetClipboard();

    public async Task<IReadOnlyList<string>> GetFormatsAsync()
    {
        IClipboard? clipboard = GetClipboard();
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
        IClipboard? clipboard = GetClipboard();
        if (clipboard is null) return null;

        if (MapFromString(format) is { } stringFormat)
        {
            return await clipboard.TryGetValueAsync(stringFormat);
        }

        return null;
    }

    public async Task<IReadOnlyList<string>?> TryGetFilePathsAsync()
    {
        IClipboard? clipboard = GetClipboard();
        if (clipboard is null) return null;

        IReadOnlyList<IStorageItem>? files = await clipboard.TryGetFilesAsync();
        if (files is null) return null;

        return SceneFileDrop.GetLocalPaths(files);
    }

    public async Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync()
    {
        IClipboard? clipboard = GetClipboard();
        if (clipboard is null) return null;

        var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null) return null;

        using var ms = new MemoryStream();
        bitmap.Save(ms, PngBitmapEncoderOptions.Default);
        return ms.ToArray();
    }

    public async Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries)
    {
        IClipboard? clipboard = GetClipboard();
        if (clipboard is null) return false;

        // The clipboard takes ownership of the transfer.
        var data = new DataTransfer();
        foreach (ClipboardEntry entry in entries)
        {
            if (MapFromString(entry.Format) is { } format)
            {
                data.Add(DataTransferItem.Create(format, entry.Text));
            }
        }

        await clipboard.SetDataAsync(data);
        return true;
    }

    private string MapToString(DataFormat format)
    {
        if (format == DataFormat.File) return BeutlClipboardFormats.Files;
        if (format == DataFormat.Bitmap) return BeutlClipboardFormats.Bitmap;
        if (format == DataFormat.Text) return BeutlClipboardFormats.Text;
        if (MapFromString(format.Identifier) == format) return format.Identifier;
        return format.ToString() ?? string.Empty;
    }

    private DataFormat<string>? MapFromString(string format)
    {
        if (format == BeutlClipboardFormats.Text) return DataFormat.Text;
        if (_stringFormat is { } customFormat && GetStringFormatName(customFormat) == format) return customFormat;
        return format switch
        {
            BeutlClipboardFormats.Element => BeutlDataFormats.Element,
            BeutlClipboardFormats.Elements => BeutlDataFormats.Elements,
            BeutlClipboardFormats.ElementGroups => BeutlDataFormats.ElementGroups,
            BeutlClipboardFormats.KeyFrame => BeutlDataFormats.KeyFrame,
            BeutlClipboardFormats.KeyFrameAnimation => BeutlDataFormats.KeyFrameAnimation,
            BeutlClipboardFormats.KeyFrameSelection => BeutlDataFormats.KeyFrameSelection,
            KnownLibraryItemFormats.EngineObject => BeutlDataFormats.EngineObject,
            KnownLibraryItemFormats.Transform => BeutlDataFormats.Transform,
            KnownLibraryItemFormats.FilterEffect => BeutlDataFormats.FilterEffect,
            KnownLibraryItemFormats.AudioEffect => BeutlDataFormats.AudioEffect,
            KnownLibraryItemFormats.Brush => BeutlDataFormats.Brush,
            KnownLibraryItemFormats.Geometry => BeutlDataFormats.Geometry,
            KnownLibraryItemFormats.Pen => BeutlDataFormats.Pen,
            _ => null,
        };
    }
}
