using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Proxy;

public sealed class FFmpegProxyGenerator(IProxyStore store) : IProxyGenerator, IProxyGeneratorAvailability
{
    private static readonly ILogger s_logger = Log.CreateLogger<FFmpegProxyGenerator>();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public bool IsAvailable => !FFmpegInstallNotifier.IsLibrariesMissing;

    public event EventHandler? AvailabilityChanged
    {
        add => FFmpegInstallNotifier.AvailabilityChanged += value;
        remove => FFmpegInstallNotifier.AvailabilityChanged -= value;
    }

    public async ValueTask GenerateAsync(ProxyJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        string sourcePath = job.Source.SourcePath;
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(null, sourcePath);

        if (IsStillImage(sourcePath))
            throw new ProxyGenerationSkippedException("Still images are not eligible for proxy generation.");

        using MediaReader reader = OpenSourceReader(sourcePath);

        if (!reader.HasVideo || reader.VideoInfo.NumFrames <= 0)
            throw new ProxyGenerationSkippedException("Source has no video stream.");

        PixelSize originalSize = reader.VideoInfo.FrameSize;
        if (originalSize.Width <= 0 || originalSize.Height <= 0)
            throw new ProxyGenerationSkippedException("Source video has no frame size.");

        PixelSize proxySize = CalculateProxySize(originalSize, job.Preset);
        string relative = ProxyPathUtilities.BuildRelativePath(job.Source, job.Preset);
        string finalPath = Path.Combine(store.StoreRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        string tempPath = CreateTempPathForOutput(finalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        await EncodeAndPublishGuardedAsync(tempPath, async () =>
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using var frameProvider = new ReaderFrameProvider(reader, job.Progress);
            using var sampleProvider = new SilentSampleProvider();
            var controller = new FFmpegEncodingControllerProxy(tempPath, new FFmpegEncodingSettings());
            Configure(controller, reader.VideoInfo, proxySize, job.Preset);

            await controller.Encode(frameProvider, sampleProvider, job.CancellationToken);

            await PublishAsync(tempPath, finalPath, job, relative, originalSize, proxySize, job.CancellationToken);
        });
    }

    // The temp artifact must not outlive a failed or canceled generation: nothing else reclaims it
    // until the age-based reconcile sweep, so every failure path deletes it here.
    internal static async Task EncodeAndPublishGuardedAsync(string tempPath, Func<Task> encodeAndPublish)
    {
        try
        {
            await encodeAndPublish();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            TryDelete(tempPath);
            throw CreateUnavailableException(ex);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

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
        Func<string, string?>? metadataBackupAttempt = null,
        Func<string, string, bool>? backupMoveAttempt = null)
    {
        ct.ThrowIfCancellationRequested();

        string? replacedFinalBackupPath = null;
        string? metadataBackupPath = null;
        ProxyEntry? entry = null;
        bool committed = false;
        try
        {
            replacedFinalBackupPath = await MoveExistingFileToBackupWithRetryAsync(finalPath, ct, backupMoveAttempt);
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
            committed = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RestoreOrDeleteCanceledFinal(finalPath, replacedFinalBackupPath);
            RestoreMetadata(finalPath, entry, metadataBackupPath);
            replacedFinalBackupPath = null;
            throw;
        }
        catch when (replacedFinalBackupPath != null)
        {
            RestoreFinalPath(finalPath, replacedFinalBackupPath);
            RestoreMetadata(finalPath, entry, metadataBackupPath);
            replacedFinalBackupPath = null;
            throw;
        }
        finally
        {
            if (committed && replacedFinalBackupPath != null)
            {
                TryDelete(replacedFinalBackupPath);
            }

            if (metadataBackupPath != null)
                TryDelete(metadataBackupPath);
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

    private static void Configure(
        FFmpegEncodingControllerProxy controller,
        VideoStreamInfo videoInfo,
        PixelSize proxySize,
        ProxyPreset preset)
    {
        ProxyEncodeParameters parameters = ProxyPresetDefinitions.Get(preset);
        var videoSettings = controller.VideoSettings;
        videoSettings.SourceSize = videoInfo.FrameSize;
        videoSettings.DestinationSize = proxySize;
        videoSettings.FrameRate = videoInfo.FrameRate;
        videoSettings.Codec = new CodecRecord("libx264", "H.264 / AVC");
        videoSettings.Options.Clear();
        videoSettings.Options.Add(new AdditionalOption("preset", parameters.Preset));
        videoSettings.Options.Add(new AdditionalOption("crf", parameters.Crf.ToString()));
        videoSettings.Options.Add(new AdditionalOption("tune", parameters.Tune));
        videoSettings.Options.Add(new AdditionalOption("profile", "high"));
        videoSettings.Options.Add(new AdditionalOption("level", "4.0"));
    }

    internal static PixelSize CalculateProxySize(PixelSize original, ProxyPreset preset)
    {
        ProxyEncodeParameters parameters = ProxyPresetDefinitions.Get(preset);
        float scale = parameters.Scale;
        int longEdge = Math.Max(original.Width, original.Height);
        if (parameters.LongEdgeClamp is { } clamp && longEdge * scale > clamp)
        {
            scale = clamp / (float)longEdge;
        }

        // Round the long edge from the single scale, then derive the short edge from the *realized*
        // long edge so both axes share one scale and the proxy aspect ratio tracks the source. The
        // even-dimension constraint still leaves an unavoidable sub-pixel AR deviation.
        if (original.Width >= original.Height)
        {
            int width = MakeEven(original.Width * scale);
            int height = MakeEven(width * (double)original.Height / original.Width);
            return new PixelSize(width, height);
        }
        else
        {
            int height = MakeEven(original.Height * scale);
            int width = MakeEven(height * (double)original.Width / original.Height);
            return new PixelSize(width, height);
        }
    }

    private static MediaReader OpenSourceReader(string sourcePath)
    {
        try
        {
            return MediaReader.Open(
                sourcePath,
                new MediaOptions(MediaMode.Video) { PreferProxy = false });
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            throw CreateUnavailableException(ex);
        }
        catch (Exception ex) when (FFmpegInstallNotifier.IsLibrariesMissing)
        {
            // The FFmpeg decoder recorded the libraries missing while opening and no fallback decoder
            // could open this source; treat it as unavailable so the queue pauses rather than draining
            // the batch as ordinary per-file failures.
            throw CreateUnavailableException(ex);
        }
    }

    private static int MakeEven(double value)
    {
        int rounded = (int)Math.Round(value / 2.0, MidpointRounding.AwayFromZero) * 2;
        return Math.Max(2, rounded);
    }

    private static bool IsStillImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png"
            or ".jpg"
            or ".jpeg"
            or ".bmp"
            or ".gif"
            or ".webp"
            or ".tif"
            or ".tiff";
    }

    private static ProxyGeneratorUnavailableException CreateUnavailableException(Exception ex)
    {
        FFmpegInstallNotifier.NotifyMissing();
        return new ProxyGeneratorUnavailableException(ex.Message);
    }

    internal static string CreateTempPathForOutput(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath) ?? string.Empty;
        string extension = Path.GetExtension(finalPath);
        string fileName = $"{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp{extension}";
        return Path.Combine(directory, fileName);
    }

    private static string CreateBackupPathForOutput(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string extension = Path.GetExtension(path);
        string fileName = $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.bak{extension}";
        return Path.Combine(directory, fileName);
    }

    // Backing up the existing proxy hits the same Windows sharing violation the temp->final move
    // does when preview playback still holds the old proxy open, so it goes through the same bounded
    // retry (the backup path is a fresh GUID, so the move never overwrites).
    private static async Task<string?> MoveExistingFileToBackupWithRetryAsync(
        string path,
        CancellationToken ct,
        Func<string, string, bool>? moveAttempt = null)
    {
        if (!File.Exists(path))
            return null;

        string backupPath = CreateBackupPathForOutput(path);
        await MoveWithRetryAsync(path, backupPath, ct, moveAttempt);
        return backupPath;
    }

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

    private static string GetMetadataPath(string finalPath)
        => Path.Combine(Path.GetDirectoryName(finalPath)!, "meta.json");

    private static void RestoreOrDeleteCanceledFinal(string finalPath, string? backupPath)
    {
        if (backupPath != null)
        {
            RestoreFinalPath(finalPath, backupPath);
        }
        else
        {
            TryDelete(finalPath);
        }
    }

    private static void RestoreFinalPath(string finalPath, string backupPath)
    {
        // Best-effort: a rollback I/O fault must not replace the primary exception. The ProxyStore
        // index is authoritative and ReconcileAsync recovers a stranded backup on next run.
        try
        {
            TryDelete(finalPath);
            if (File.Exists(backupPath))
                File.Move(backupPath, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to restore previous proxy at {Path} during rollback.", finalPath);
        }
    }

    private static void RestoreMetadata(string finalPath, ProxyEntry? entry, string? backupPath)
    {
        try
        {
            if (backupPath != null)
            {
                File.Copy(backupPath, GetMetadataPath(finalPath), overwrite: true);
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

    internal static void WriteMetadata(string finalPath, ProxyEntry entry)
    {
        string metadataPath = GetMetadataPath(finalPath);
        ProxyEntry[] entries = ReadMetadataEntries(metadataPath, entry.Source)
            .Where(existing => existing.Preset != entry.Preset)
            .Append(entry)
            .ToArray();
        var metadata = new ProxySourceMetadata
        {
            Source = entry.Source,
            Entries = [.. entries],
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
    }

    private static void RemoveMetadataEntry(string finalPath, ProxyEntry entry)
    {
        try
        {
            string metadataPath = Path.Combine(Path.GetDirectoryName(finalPath)!, "meta.json");
            if (!File.Exists(metadataPath))
                return;

            ProxyEntry[] entries = ReadMetadataEntries(metadataPath, entry.Source)
                .Where(existing => existing.Preset != entry.Preset)
                .ToArray();
            if (entries.Length == 0)
            {
                File.Delete(metadataPath);
                return;
            }

            var metadata = new ProxySourceMetadata
            {
                Source = entry.Source,
                Entries = [.. entries],
            };
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
        }
        catch
        {
        }
    }

    private static IEnumerable<ProxyEntry> ReadMetadataEntries(string metadataPath, ProxyFingerprint source)
    {
        if (!File.Exists(metadataPath))
            yield break;

        ProxySourceMetadata? metadata = null;
        try
        {
            metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(
                File.ReadAllText(metadataPath),
                s_jsonOptions);
        }
        catch
        {
        }

        if (metadata is not { Version: ProxySourceMetadata.CurrentVersion }
            || metadata.Source != source)
        {
            yield break;
        }

        foreach (ProxyEntry entry in metadata.Entries)
        {
            if (entry.Source == source)
                yield return entry;
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

    // Bounded retry for File.Move: on Windows, a preview reader holding dest open causes a transient
    // sharing violation (IOException); on Unix the replace is atomic so the retry is a no-op. A
    // genuinely-held-long file still fails after maxAttempts — the job fails and the old proxy is kept,
    // the same end state as the un-retried move. The moveAttempt seam lets tests inject failures; the
    // default does File.Move(overwrite: true) and returns false on IOException so the helper retries.
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
            File.Move(source, dest, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private sealed class ReaderFrameProvider(
        MediaReader reader,
        IProgress<ProxyJobProgress>? progress) : IFrameProvider
    {
        public long FrameCount => reader.VideoInfo.NumFrames;

        public Rational FrameRate => reader.VideoInfo.FrameRate;

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            if (!reader.ReadVideo((int)frame, out Ref<Bitmap>? bitmapRef))
                throw new InvalidOperationException($"Could not decode source frame {frame}.");

            using (bitmapRef)
            {
                progress?.Report(new ProxyJobProgress(
                    FrameCount <= 0 ? 0 : Math.Clamp((frame + 1) / (double)FrameCount, 0, 1),
                    null));

                return ValueTask.FromResult(bitmapRef.Value.Clone());
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class SilentSampleProvider : ISampleProvider
    {
        public long SampleCount => 0;

        public long SampleRate => 44100;

        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
        {
            return ValueTask.FromResult(new Pcm<Stereo32BitFloat>((int)SampleRate, (int)Math.Max(0, length)));
        }

        public void Dispose()
        {
        }
    }
}
