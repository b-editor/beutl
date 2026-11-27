using Avalonia.Media.Imaging;
using Beutl.Agents;
using Beutl.IO;

namespace Beutl.Editor.Components.AgentTab;

internal sealed class AgentAttachmentStore(string beutlHome)
{
    internal const string PastedImagePrefix = "paste-";
    internal const string PastedTextMediaType = "text/plain";

    public Task<AgentAttachedFile> SavePastedImageAsync(Bitmap image, CancellationToken cancellationToken) =>
        SaveAsync(PastedImagePrefix, ".png", "image/png", stream => image.Save(stream, PngBitmapEncoderOptions.Default),
            cancellationToken);

    public Task<AgentAttachedFile> SavePastedTextAsync(string text, CancellationToken cancellationToken) =>
        SaveAsync("pasted-text-", ".txt", PastedTextMediaType, stream =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(text);
        }, cancellationToken);

    public Task<AgentAttachedFile> SaveAsync(
        string prefix, string extension, string mediaType, Action<Stream> write, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = Path.Combine(beutlHome, "agent-threads", "attachments");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{prefix}{Guid.NewGuid():N}{extension}");
            using var output = new StagedOutputFile(path);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(output.TemporaryPath, options))
                write(stream);
            output.Commit(cancellationToken);
            return new AgentAttachedFile(path, mediaType);
        }, cancellationToken);
}
