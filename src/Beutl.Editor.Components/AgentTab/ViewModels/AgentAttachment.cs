using System.Text;
using Avalonia.Media.Imaging;
using Beutl.Agents;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed class AgentAttachment : AgentBindable, IDisposable
{
    private const int ThumbnailExtent = 216;
    internal const long MaxImageBytes = 20L * 1024 * 1024;
    private readonly Action<AgentAttachment> _remove;

    private AgentAttachment(AgentAttachedFile file, string label, string detail, Icon icon,
        Bitmap? thumbnail, Action<AgentAttachment> remove, string? preview = null)
    {
        File = file;
        Label = label;
        Detail = detail;
        Icon = icon;
        Thumbnail = thumbnail;
        _remove = remove;
        Tooltip = preview ?? file.Path;
    }

    public AgentAttachedFile File { get; }
    public string Path => File.Path;
    public string Label { get; }
    public string Detail { get; }
    public Icon Icon { get; }
    public Bitmap? Thumbnail { get; private set => Set(ref field, value); }
    public bool HasThumbnail => Thumbnail is not null;
    public bool IsPastedText => File.MediaType == AgentAttachmentStore.PastedTextMediaType;
    public string RemoveLabel => AgentStrings.RemoveAttachment;
    public string Tooltip { get; }

    public AgentUserAttachment Snapshot() => new(Path, Label, File.MediaType, Detail, Icon == Icon.Image);

    public static AgentAttachment CreateFile(AgentAttachedFile file, Action<AgentAttachment> remove)
    {
        if (file.MediaType == AgentAttachmentStore.PastedTextMediaType)
        {
            var (detail, preview) = ReadPastedText(file.Path);
            return new AgentAttachment(file, AgentStrings.PastedText, detail, Icon.Document, null, remove, preview);
        }

        string name = System.IO.Path.GetFileName(file.Path);
        string label = file.MediaType == "image/png"
            ? name.StartsWith(AgentAttachmentStore.PastedImagePrefix, StringComparison.Ordinal)
                ? AgentStrings.PastedImage
                : AgentStrings.CurrentFrame
            : name;
        string extension = System.IO.Path.GetExtension(file.Path).TrimStart('.').ToLowerInvariant();
        string? type = file.MediaType?.Split('/', 2)[0].ToLowerInvariant();
        var icon = (type, extension) switch
        {
            ("image", _) or (_, "png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp" or "tif" or "tiff" or "avif" or "ico") => Icon.Image,
            ("video", _) or (_, "mp4" or "m4v" or "mov" or "mkv" or "avi" or "webm" or "wmv" or "mpeg" or "mpg") => Icon.Video,
            ("audio", _) or (_, "mp3" or "wav" or "m4a" or "aac" or "flac" or "ogg" or "opus" or "aiff") => Icon.MusicNote2,
            _ => Icon.Document
        };
        return new AgentAttachment(file, label, extension.ToUpperInvariant(), icon,
            icon == Icon.Image ? DecodeThumbnail(file.Path) : null, remove);
    }

    public void Remove() => _remove(this);

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
        Notify(nameof(HasThumbnail));
    }

    public static bool ShouldCollapsePaste(string text)
    {
        if (text.Length >= 1500)
            return true;
        char previous = '\0';
        return CountLineBreaks(text, ref previous) >= 29;
    }

    private static (string Detail, string? Preview) ReadPastedText(string path)
    {
        const int previewLength = 240;
        try
        {
            using var reader = System.IO.File.OpenText(path);
            var preview = new StringBuilder(previewLength);
            Span<char> buffer = stackalloc char[1024];
            long lines = 1;
            long characters = 0;
            char previous = '\0';
            int read;
            while ((read = reader.Read(buffer)) > 0)
            {
                characters += read;
                lines += CountLineBreaks(buffer[..read], ref previous);
                preview.Append(buffer[..Math.Min(read, previewLength - preview.Length)]);
            }
            if (characters > preview.Length)
            {
                if (char.IsHighSurrogate(preview[^1]))
                    preview.Length--;
                preview.Append('…');
            }
            return (string.Format(AgentStrings.PastedTextDetail, lines, characters), preview.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ("", null);
        }
    }

    private static int CountLineBreaks(ReadOnlySpan<char> text, ref char previous)
    {
        int count = 0;
        foreach (char character in text)
        {
            if (character == '\r' || (character == '\n' && previous != '\r'))
                count++;
            previous = character;
        }
        return count;
    }

    internal static Bitmap? DecodeThumbnail(string path) => AgentImagePreview.DecodeFile(path, ThumbnailExtent, ThumbnailExtent);
}
