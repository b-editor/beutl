using Avalonia.Platform.Storage;
using Beutl.Editor.Services.Captions;

namespace Beutl.Editor.Components.Helpers;

internal static class CaptionExportStorage
{
    public static async Task WriteAsync(
        IStorageFile file,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        // Every desktop save picker hands out files with a local path.
        Uri path = file.Path;
        if (!path.IsFile || string.IsNullOrWhiteSpace(path.LocalPath))
            throw new NotSupportedException("Captions can only be exported to a local file.");

        await CaptionFileWriter.WriteAsync(path.LocalPath, bytes, overwrite: true, cancellationToken).ConfigureAwait(false);
    }
}
