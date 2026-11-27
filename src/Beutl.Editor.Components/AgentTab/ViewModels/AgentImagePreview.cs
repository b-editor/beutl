using Avalonia.Media.Imaging;
using Beutl.Agents;
using Beutl.Editor.Services.AI;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed class AgentImagePreview(Bitmap thumbnail, string caption, string? path, Func<Bitmap?> load) : IDisposable
{
    private const long MaxToolImageBytes = 16 * 1024 * 1024;
    private const int MaxToolThumbnailWidth = 320;
    private const int MaxToolThumbnailHeight = 240;

    public Bitmap Thumbnail { get; } = thumbnail;
    public string Caption { get; } = caption;
    public string? Path { get; } = path;

    public Bitmap? Load() => load();

    public void Dispose() => Thumbnail.Dispose();

    public static AgentImagePreview? FromFile(string path, string caption, Func<string, bool>? canOpen = null)
        => canOpen?.Invoke(path) != false && AgentAttachment.DecodeThumbnail(path) is { } bitmap
            ? new(bitmap, caption, path, () => canOpen?.Invoke(path) != false ? LoadFile(path) : null)
            : null;

    public static Bitmap? LoadFile(string path) => DecodeFile(path);

    internal static Bitmap? DecodeFile(string path, int? maxWidth = null, int? maxHeight = null)
        => Decode(() => File.OpenRead(path), AgentAttachment.MaxImageBytes, maxWidth, maxHeight);

    public static AgentImagePreview? FromToolOutput(AgentToolOutput output, string caption)
    {
        string? path = Uri.TryCreate(output.Uri, UriKind.Absolute, out var uri) && uri.IsFile
            && !uri.IsUnc && string.IsNullOrEmpty(uri.Host) ? uri.LocalPath : null;
        Stream? Open()
        {
            if (output.Data is not { Length: > 0 } data)
                return path is null ? null : File.OpenRead(path);
            return data.Length <= (MaxToolImageBytes + 2) / 3 * 4
                ? new MemoryStream(Convert.FromBase64String(data), writable: false) : null;
        }

        return Decode(Open, MaxToolImageBytes, MaxToolThumbnailWidth, MaxToolThumbnailHeight) is { } bitmap
            ? new AgentImagePreview(bitmap, caption, path, () => Decode(Open, MaxToolImageBytes))
            : null;
    }

    private static Bitmap? Decode(Func<Stream?> open, long maxBytes, int? maxWidth = null, int? maxHeight = null)
    {
        try
        {
            using Stream? stream = open();
            if (stream is null)
                return null;
            var (width, height) = AiImageDecodeValidator.ValidateEncoded(stream, maxBytes);
            if (maxWidth is null && maxHeight is null)
                return new Bitmap(stream);
            int limitWidth = maxWidth ?? int.MaxValue;
            int limitHeight = maxHeight ?? int.MaxValue;
            if (width <= limitWidth && height <= limitHeight)
                return new Bitmap(stream);
            return (double)width / limitWidth >= (double)height / limitHeight
                ? Bitmap.DecodeToWidth(stream, limitWidth) : Bitmap.DecodeToHeight(stream, limitHeight);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
