using Avalonia.Media.Imaging;
using Beutl.Agents;
using Beutl.Editor.Services.AI;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed class AgentImagePreview(Bitmap thumbnail, string caption, string? path, Func<Bitmap?> load) : IDisposable
{
    private const long MaxToolImageBytes = 16 * 1024 * 1024;

    public Bitmap Thumbnail { get; } = thumbnail;
    public string Caption { get; } = caption;
    public string? Path { get; } = path;

    public Bitmap? Load() => load();

    public void Dispose() => Thumbnail.Dispose();

    public static AgentImagePreview? FromFile(string path, string caption)
        => AgentAttachment.DecodeThumbnail(path) is { } bitmap ? new(bitmap, caption, path, () => LoadFile(path)) : null;

    public static Bitmap? LoadFile(string path) => Decode(() => File.OpenRead(path), AgentAttachment.MaxImageBytes);

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

        return Decode(Open, MaxToolImageBytes, thumbnail: true) is { } bitmap
            ? new AgentImagePreview(bitmap, caption, path, () => Decode(Open, MaxToolImageBytes))
            : null;
    }

    private static Bitmap? Decode(Func<Stream?> open, long maxBytes, bool thumbnail = false)
    {
        try
        {
            using Stream? stream = open();
            if (stream is null)
                return null;
            var (width, height) = AiImageDecodeValidator.ValidateEncoded(stream, maxBytes);
            const int maxWidth = 320;
            const int maxHeight = 240;
            if (!thumbnail || (width <= maxWidth && height <= maxHeight))
                return new Bitmap(stream);
            return (double)width / maxWidth >= (double)height / maxHeight
                ? Bitmap.DecodeToWidth(stream, maxWidth) : Bitmap.DecodeToHeight(stream, maxHeight);
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
