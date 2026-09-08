using Avalonia.Platform.Storage;

namespace Beutl.Services.AI;

internal static class CaptionExportStorage
{
    public static async Task WriteAsync(
        IStorageFile file,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Uri path = file.Path;
        if (path.IsFile && !string.IsNullOrWhiteSpace(path.LocalPath))
        {
            string destinationPath = path.LocalPath;
            string temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                UnixFileMode destinationMode = default;
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                };
                if (!OperatingSystem.IsWindows())
                {
                    destinationMode = File.Exists(destinationPath)
                        ? File.GetUnixFileMode(destinationPath)
                        : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    options.UnixCreateMode = destinationMode;
                }

                await using (var stream = new FileStream(temporaryPath, options))
                {
                    await stream.WriteAsync(bytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporaryPath, destinationMode);
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        else
        {
            await WriteNonLocalCaptionExportAsync(file, bytes, cancellationToken);
        }
    }

    private static async Task WriteNonLocalCaptionExportAsync(
        IStorageFile destination,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IStorageFolder parent = await destination.GetParentAsync()
            ?? throw new NotSupportedException(
                "The storage provider cannot stage a safe replacement for this caption file.");
        IStorageFolder? stagingFolder = null;
        IStorageFolder? backupFolder = null;
        IStorageFile? stagedFile = null;
        IStorageFile? backupFile = null;
        bool originalIsInBackup = false;
        bool preserveRecoveryArtifacts = false;
        try
        {
            string transaction = Guid.NewGuid().ToString("N");
            stagingFolder = await parent.CreateFolderAsync($"beutl-caption-{transaction}-stage")
                ?? throw new NotSupportedException(
                    "The storage provider cannot create a caption staging folder.");
            stagedFile = await stagingFolder.CreateFileAsync(destination.Name)
                ?? throw new NotSupportedException(
                    "The storage provider cannot create a staged caption file.");

            await using (Stream stream = await stagedFile.OpenWriteAsync())
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            backupFolder = await parent.CreateFolderAsync($"beutl-caption-{transaction}-backup")
                ?? throw new NotSupportedException(
                    "The storage provider cannot create a caption backup folder.");
            IStorageItem? movedOriginal;
            try
            {
                movedOriginal = await destination.MoveAsync(backupFolder);
            }
            catch
            {
                preserveRecoveryArtifacts = true;
                throw;
            }
            if (movedOriginal is not IStorageFile movedOriginalFile)
            {
                movedOriginal?.Dispose();
                preserveRecoveryArtifacts = true;
                throw new IOException("The storage provider could not move the previous caption file to safety.");
            }
            backupFile = movedOriginalFile;
            originalIsInBackup = true;

            try
            {
                IStorageItem? movedStaged = await stagedFile.MoveAsync(parent);
                if (movedStaged is not IStorageFile committed)
                {
                    movedStaged?.Dispose();
                    throw new IOException("The storage provider could not publish the staged caption file.");
                }
                TryDisposeStorageItem(committed);
                originalIsInBackup = false;
            }
            catch (Exception commitException)
            {
                Exception? rollbackException = await TryRestoreNonLocalCaptionAsync(
                    parent,
                    backupFile,
                    destination.Name);
                if (rollbackException is not null)
                {
                    preserveRecoveryArtifacts = true;
                    throw new AggregateException(
                        "Caption export failed and the previous file could not be restored; its backup was preserved.",
                        commitException,
                        rollbackException);
                }

                originalIsInBackup = false;
                throw;
            }

            await TryDeleteStorageItemAsync(backupFile);
        }
        finally
        {
            if (!preserveRecoveryArtifacts)
            {
                await TryDeleteStorageItemAsync(stagedFile);
                if (!originalIsInBackup)
                {
                    await TryDeleteStorageItemAsync(stagingFolder);
                    await TryDeleteStorageItemAsync(backupFolder);
                }
            }

            TryDisposeStorageItem(backupFile);
            TryDisposeStorageItem(stagedFile);
            TryDisposeStorageItem(backupFolder);
            TryDisposeStorageItem(stagingFolder);
            TryDisposeStorageItem(parent);
        }
    }

    private static async Task<Exception?> TryRestoreNonLocalCaptionAsync(
        IStorageFolder parent,
        IStorageFile backupFile,
        string destinationName)
    {
        try
        {
            IStorageFile? failedReplacement = await parent.GetFileAsync(destinationName);
            try
            {
                if (failedReplacement is not null)
                {
                    await failedReplacement.DeleteAsync();
                }
            }
            finally
            {
                TryDisposeStorageItem(failedReplacement);
            }

            IStorageItem restored = await backupFile.MoveAsync(parent)
                ?? throw new IOException("The storage provider returned no restored caption file.");
            TryDisposeStorageItem(restored);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task TryDeleteStorageItemAsync(IStorageItem? item)
    {
        if (item is null)
            return;

        try
        {
            await item.DeleteAsync();
        }
        catch
        {
            // A completed replacement must not be reported as failed only because a provider could
            // not remove its empty staging folder. If rollback failed, recovery artifacts are kept.
        }
    }

    private static void TryDisposeStorageItem(IDisposable? item)
    {
        try
        {
            item?.Dispose();
        }
        catch
        {
            // Storage handles are cleanup-only here; the write or rollback outcome is authoritative.
        }
    }

}
