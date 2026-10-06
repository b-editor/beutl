using Beutl.Media;
using Beutl.Media.Proxy;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Proxy;

public sealed partial class FFmpegProxyGenerator
{
    // Encode has returned; observe the job token before the move + registration so a cancellation
    // that arrived during the final IPC round-trip does not still publish the artifact.
    internal async Task PublishAsync(
        string tempPath,
        string finalPath,
        ProxyJob job,
        string relative,
        PixelSize originalSize,
        PixelSize proxySize,
        CancellationToken ct,
        Func<string, string, bool>? moveAttempt = null,
        Func<string, string?>? metadataBackupAttempt = null)
    {
        ct.ThrowIfCancellationRequested();

        // Every generation owns a new path. Never rename or overwrite a file that an existing
        // preview/filmstrip reader may still have open, including legacy <preset>.mp4 proxies.
        if (File.Exists(finalPath))
            throw new IOException("A proxy generation must publish to a new file path.");

        ProxyEntry? previous = store.TryGet(job.Source, job.Preset);
        string? metadataBackupPath = null;
        ProxyEntry? entry = null;
        try
        {
            await MoveWithRetryAsync(tempPath, finalPath, ct, moveAttempt);
            ct.ThrowIfCancellationRequested();

            // The encoded proxy is now on disk at finalPath and is valid. A failure in the metadata /
            // registration step below must never delete it — the artifact is re-registerable, so a
            // recoverable failure is surfaced instead of destroying it.
            long fileSize = new FileInfo(finalPath).Length;
            ct.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            entry = new ProxyEntry(
                job.Source,
                job.Preset,
                ProxyState.Ready,
                relative,
                fileSize,
                originalSize,
                proxySize,
                now,
                now,
                null);

            metadataBackupPath = TryCopyExistingFileToBackup(GetMetadataPath(finalPath), metadataBackupAttempt);
            await FinalizeAsync(finalPath, entry, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (RestoreCanceledStoreEntry(entry, previous))
            {
                TryDelete(finalPath);
                RestoreMetadata(finalPath, entry, metadataBackupPath, previous);
            }
            throw;
        }
        catch
        {
            // Preserve recoverable sidecars for a first generation whose registration failed. A
            // failed replacement instead keeps the metadata for the still-usable previous entry.
            if (metadataBackupPath != null || previous != null)
                RestoreMetadata(finalPath, entry, metadataBackupPath, previous);
            throw;
        }
        finally
        {
            if (metadataBackupPath != null)
                TryDelete(metadataBackupPath);
        }
    }

    private bool RestoreCanceledStoreEntry(ProxyEntry? entry, ProxyEntry? previous)
    {
        if (entry is null)
            return true;

        try
        {
            // Register may have updated the store before throwing. Roll back only our generation;
            // a concurrently registered replacement must keep its own entry.
            if (store.TryGet(entry.Source, entry.Preset)?.ProxyFileRelative == entry.ProxyFileRelative)
            {
                if (previous != null)
                    store.Register(previous);
                else
                    store.Delete(entry.Source, entry.Preset);
            }

            return store.TryGet(entry.Source, entry.Preset)?.ProxyFileRelative != entry.ProxyFileRelative;
        }
        catch (Exception ex)
        {
            // Preserve a possibly referenced artifact and its metadata if rollback fails, rather
            // than leave a store entry pointing at a file we just deleted. Keep the cancellation.
            s_logger.LogWarning(ex, "Failed to restore proxy store entry after cancellation for {Path}", entry.ProxyFileRelative);
            return false;
        }
    }

    internal async Task FinalizeAsync(string finalPath, ProxyEntry entry, CancellationToken ct = default)
    {
        // The proxy is already encoded and moved to finalPath; a sidecar-write failure (e.g. a
        // transient lock/permission error) must not skip the index registration, or the ready proxy
        // would be neither indexed nor recoverable from a sidecar and would later look like an orphan.
        ct.ThrowIfCancellationRequested();
        try
        {
            WriteMetadata(finalPath, entry);
        }
        catch (Exception ex)
        {
            // Sidecar is best-effort: the ProxyStore index is the authoritative record, and
            // ReconcileAsync can later recover the artifact from disk. Log and continue.
            s_logger.LogWarning(ex, "Failed to write proxy sidecar metadata at {Path}", finalPath);
        }

        ct.ThrowIfCancellationRequested();
        await RegisterWithRetryAsync(entry, ct);
    }

    private async Task RegisterWithRetryAsync(ProxyEntry entry, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                store.Register(entry);
                return;
            }
            catch when (attempt < maxAttempts)
            {
                // Transient contention (e.g. index-lock) on a valid, already-moved artifact: back off
                // briefly and retry rather than failing the whole job.
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), ct);
            }
        }
    }

    private static string CreateBackupPathForOutput(string path)
        => CreateSiblingPath(path, "bak");

    private static string? CopyExistingFileToBackup(string path)
    {
        if (!File.Exists(path))
            return null;

        string backupPath = CreateBackupPathForOutput(path);
        File.Copy(path, backupPath, overwrite: false);
        return backupPath;
    }

    private static string? TryCopyExistingFileToBackup(string path, Func<string, string?>? copyAttempt = null)
    {
        try
        {
            return (copyAttempt ?? CopyExistingFileToBackup)(path);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to back up proxy sidecar metadata at {Path}; continuing with index registration.", path);
            return null;
        }
    }

    private static void RestoreMetadata(string finalPath, ProxyEntry? entry, string? backupPath, ProxyEntry? previous)
    {
        try
        {
            if (backupPath != null)
            {
                File.Copy(backupPath, GetMetadataPath(finalPath), overwrite: true);
            }
            else if (previous is { State: ProxyState.Ready or ProxyState.Stale })
            {
                WriteMetadata(finalPath, previous);
            }
            else if (entry != null)
            {
                RemoveMetadataEntry(finalPath, entry);
            }
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to restore proxy sidecar metadata at {Path} during rollback.", finalPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    // Retry transient filesystem failures when publishing a fresh generation. Never overwrite an
    // existing generation; readers keep their immutable file until they release its pin.
    internal static async Task MoveWithRetryAsync(
        string source,
        string dest,
        CancellationToken ct,
        Func<string, string, bool>? moveAttempt = null,
        int maxAttempts = 5,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Must be at least 1.");

        Func<string, string, bool> attempt = moveAttempt ?? DefaultMoveAttempt;
        TimeSpan delay = retryDelay ?? TimeSpan.FromMilliseconds(200);

        IOException? lastError = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (attempt(source, dest))
                    return;
            }
            catch (IOException ex)
            {
                // A delegate may throw IOException instead of returning false; preserve it so the
                // exhausted path rethrows the underlying error rather than a synthetic one.
                lastError = ex;
            }

            if (i < maxAttempts - 1)
                await Task.Delay(delay, ct);
        }

        throw lastError ?? new IOException(
            $"Failed to move '{source}' to '{dest}' after {maxAttempts} attempt(s).");
    }

    private static bool DefaultMoveAttempt(string source, string dest)
    {
        try
        {
            File.Move(source, dest, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
