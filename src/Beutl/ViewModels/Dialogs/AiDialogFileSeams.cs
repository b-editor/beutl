using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The result of an AI dialog save picker. The picker and the file replacement
/// are kept as separate steps so identity fencing can reject a late destination
/// before it mutates that destination.
/// </summary>
internal sealed record AiSaveFileDestination(string Path);

/// <summary>
/// The main window's storage provider, and the save picker the AI dialogs open on it.
/// </summary>
internal static class AiDialogStorage
{
    public static IStorageProvider? MainWindowStorage()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
        {
            return null;
        }
        return TopLevel.GetTopLevel(window)?.StorageProvider;
    }

    /// <summary>
    /// Asks <paramref name="seam"/> when a test set one; otherwise opens the main window's save
    /// picker at <paramref name="startFolder"/>. The picked file is handed back for the caller to
    /// dispose once it has written the destination.
    /// </summary>
    public static async Task<(AiSaveFileDestination? Destination, IStorageFile? File)> PickSaveDestinationAsync(
        Func<CancellationToken, Task<AiSaveFileDestination?>>? seam,
        Func<FilePickerSaveOptions> createOptions,
        WellKnownFolder startFolder,
        CancellationToken cancellationToken)
    {
        if (seam is not null)
            return (await seam(cancellationToken), null);

        if (MainWindowStorage() is not { } storage)
            return (null, null);
        FilePickerSaveOptions options = createOptions();
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(startFolder);
        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        return (file is null ? null : new AiSaveFileDestination(file.Path.LocalPath), file);
    }
}

internal static class AiImageFileFormat
{
    public static void ValidatePngDestination(string path)
    {
        if (!string.Equals(
                System.IO.Path.GetExtension(path),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "AI-generated PNG images must be saved with a .png extension.");
        }
    }
}

internal static class AiAtomicFileWriter
{
    public static void Write(
        string destinationPath,
        Action<Stream> write,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);

        string fullDestinationPath = System.IO.Path.GetFullPath(destinationPath);
        string temporaryPath = fullDestinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnixFileMode destinationMode = default;
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                destinationMode = File.Exists(fullDestinationPath)
                    ? File.GetUnixFileMode(fullDestinationPath)
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                options.UnixCreateMode = destinationMode;
            }

            using (var stream = new FileStream(
                       temporaryPath,
                       options))
            {
                write(stream);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, destinationMode);
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static void WritePng(
        string destinationPath,
        Bitmap bitmap,
        string encodeFailure,
        CancellationToken cancellationToken)
        => Write(
            destinationPath,
            stream =>
            {
                if (!bitmap.Save(stream, EncodedImageFormat.Png))
                    throw new IOException(encodeFailure);
            },
            cancellationToken);
}
