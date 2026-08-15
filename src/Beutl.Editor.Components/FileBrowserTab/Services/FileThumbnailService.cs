using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

public sealed record MediaFileInfo(
    int? Width,
    int? Height,
    TimeSpan? Duration,
    double? FrameRate,
    string? VideoCodec,
    string? AudioCodec,
    int? SampleRate,
    int? NumChannels,
    long FileSize)
{
    public string ToDisplayString()
    {
        var parts = new List<string>();

        if (Width.HasValue && Height.HasValue)
        {
            parts.Add($"{Width}×{Height}");
        }

        if (FrameRate.HasValue)
        {
            parts.Add($"{FrameRate.Value:0.##}fps");
        }

        if (VideoCodec != null)
        {
            parts.Add(VideoCodec);
        }
        else if (AudioCodec != null)
        {
            parts.Add(AudioCodec);
        }

        if (SampleRate.HasValue)
        {
            parts.Add($"{SampleRate.Value}Hz");
        }

        if (NumChannels.HasValue)
        {
            parts.Add(NumChannels.Value switch
            {
                1 => "Mono",
                2 => "Stereo",
                _ => $"{NumChannels.Value}ch"
            });
        }

        if (Duration.HasValue)
        {
            parts.Add(Duration.Value.TotalHours >= 1
                ? Duration.Value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : Duration.Value.ToString(@"m\:ss", CultureInfo.InvariantCulture));
        }

        parts.Add(FormatFileSize(FileSize));

        return string.Join(" · ", parts);
    }

    public static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.#} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:0.#} MB",
            >= 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes} B"
        };
    }
}

public sealed class FileThumbnailService : IDisposable
{
    private readonly record struct CachedThumbnail(WeakReference<Bitmap> Bitmap, DateTime LastWriteUtc);

    private const int MaxThumbnailCacheEntries = 1000;
    private const int MaxMediaInfoCacheEntries = 500;
    private static readonly TimeSpan s_pruneInterval = TimeSpan.FromSeconds(60);

    private static readonly Lazy<FileThumbnailService> s_instance = new(() => new FileThumbnailService());
    private readonly ConcurrentDictionary<string, CachedThumbnail> _cache = new();
    private readonly ConcurrentDictionary<string, (MediaFileInfo Info, long LastAccessTicks)> _mediaInfoCache = new();
    private readonly SemaphoreSlim _semaphore = new(4); // 同時生成数を制限
    private readonly ILogger _logger = Log.CreateLogger<FileThumbnailService>();
    private readonly Timer _pruneTimer;
    private bool _disposed;

    private FileThumbnailService()
    {
        _pruneTimer = new Timer(_ => PruneCaches(), null, s_pruneInterval, s_pruneInterval);
    }

    public static FileThumbnailService Instance => s_instance.Value;

    public int ThumbnailSize { get; set; } = 64;

    public async Task<Bitmap?> GetThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        DateTime lastWriteUtc = GetLastWriteTimeOrDefault(filePath);

        // キャッシュを確認
        if (TryGetCached(filePath, lastWriteUtc, out Bitmap? cached))
        {
            return cached;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // 再度キャッシュを確認（競合回避）
            if (TryGetCached(filePath, lastWriteUtc, out cached))
            {
                return cached;
            }

            Bitmap? thumbnail = DecoderFileExtensions.Classify(filePath) switch
            {
                MediaFileKind.Image => await GenerateImageThumbnailAsync(filePath, cancellationToken),
                MediaFileKind.Video => await GenerateVideoThumbnailAsync(filePath, cancellationToken),
                _ when IsObjectTemplateFile(filePath) =>
                    await GenerateTemplateThumbnailAsync(filePath, cancellationToken),
                _ => null
            };

            if (thumbnail != null)
            {
                PruneThumbnailCacheIfNeeded();
                _cache[filePath] = new CachedThumbnail(new WeakReference<Bitmap>(thumbnail), lastWriteUtc);
            }

            return thumbnail;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to generate thumbnail for {FilePath}", filePath);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<MediaFileInfo?> GetMediaInfoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        if (_mediaInfoCache.TryGetValue(filePath, out var entry))
        {
            // LRU: 最終アクセス時刻を更新
            _mediaInfoCache.TryUpdate(filePath, (entry.Info, Environment.TickCount64), entry);
            return entry.Info;
        }

        MediaFileKind kind = DecoderFileExtensions.Classify(filePath);
        if (kind is not (MediaFileKind.Video or MediaFileKind.Audio))
            return null;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_mediaInfoCache.TryGetValue(filePath, out entry))
            {
                _mediaInfoCache.TryUpdate(filePath, (entry.Info, Environment.TickCount64), entry);
                return entry.Info;
            }

            var info = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    long fileSize = new FileInfo(filePath).Length;
                    using MediaReader? reader = OpenForProbe(filePath, kind);
                    if (reader == null)
                        return new MediaFileInfo(null, null, null, null, null, null, null, null, fileSize);

                    int? width = null, height = null;
                    double? frameRate = null;
                    string? videoCodec = null;
                    TimeSpan? duration = null;

                    if (reader.HasVideo)
                    {
                        var vi = reader.VideoInfo;
                        width = vi.FrameSize.Width;
                        height = vi.FrameSize.Height;
                        frameRate = vi.FrameRate.ToDouble();
                        videoCodec = vi.CodecName;
                        duration = TimeSpan.FromSeconds(vi.Duration.ToDouble());
                    }

                    string? audioCodec = null;
                    int? sampleRate = null;
                    int? numChannels = null;

                    if (reader.HasAudio)
                    {
                        var ai = reader.AudioInfo;
                        audioCodec = ai.CodecName;
                        sampleRate = ai.SampleRate;
                        numChannels = ai.NumChannels;
                        duration ??= TimeSpan.FromSeconds(ai.Duration.ToDouble());
                    }

                    return new MediaFileInfo(width, height, duration, frameRate, videoCodec, audioCodec, sampleRate, numChannels, fileSize);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to get media info for {FilePath}", filePath);
                    return null;
                }
            }, cancellationToken);

            if (info != null)
            {
                _mediaInfoCache[filePath] = (info, Environment.TickCount64);
                EvictMediaInfoCacheIfNeeded();
            }

            return info;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get media info for {FilePath}", filePath);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // An extension does not decide which streams a file carries: AVFoundation and Media Foundation
    // both claim '.adts' as video and as audio, and AVFReader throws outright when a requested
    // stream is absent — so neither asking for both nor trusting the classification alone works.
    // The classified kind is tried first and the other kind only as a fallback.
    private static MediaReader? OpenForProbe(string filePath, MediaFileKind kind)
    {
        MediaMode first = kind == MediaFileKind.Audio ? MediaMode.Audio : MediaMode.Video;
        MediaMode second = first == MediaMode.Video ? MediaMode.Audio : MediaMode.Video;

        return TryOpen(filePath, first) ?? TryOpen(filePath, second);

        static MediaReader? TryOpen(string filePath, MediaMode mode)
        {
            try
            {
                return DecoderRegistry.OpenMediaFile(filePath, new MediaOptions(mode));
            }
            catch
            {
                return null;
            }
        }
    }

    public bool CanGetMediaInfo(string filePath)
    {
        return DecoderFileExtensions.Classify(filePath) is MediaFileKind.Video or MediaFileKind.Audio;
    }

    public bool IsMediaFile(string filePath)
    {
        return DecoderFileExtensions.IsMedia(filePath);
    }

    /// <summary>
    /// Whether <paramref name="filePath"/> is an object template, whose thumbnail is the preview
    /// embedded when it was saved.
    /// </summary>
    /// <remarks>
    /// Scoped to the templates directory so browsing an unrelated folder of JSON does not parse
    /// every file looking for a template.
    /// </remarks>
    public bool IsObjectTemplateFile(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase)
               && PathScope.IsUnderDirectory(filePath, BeutlEnvironment.GetTemplatesDirectoryPath());
    }

    private async Task<Bitmap?> GenerateImageThumbnailAsync(string filePath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = File.OpenRead(filePath);
            using var original = SKBitmap.Decode(stream);
            if (original == null)
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            return ToThumbnail(original, cancellationToken);
        }, cancellationToken);
    }

    // Every path funnels through here so nothing keeps a full-resolution bitmap alive: a directory
    // enumeration starts a load per file and each item holds its result strongly, so the size the
    // browser draws at is the size it retains.
    private Bitmap? ToThumbnail(SKBitmap source, CancellationToken cancellationToken)
    {
        // アスペクト比を維持してリサイズ
        float scale = Math.Min((float)ThumbnailSize / source.Width, (float)ThumbnailSize / source.Height);
        int newWidth = Math.Max(1, (int)(source.Width * scale));
        int newHeight = Math.Max(1, (int)(source.Height * scale));

        using var resized = source.Resize(
            new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized == null)
            return null;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);

        cancellationToken.ThrowIfCancellationRequested();

        using var memStream = new MemoryStream();
        data.SaveTo(memStream);
        memStream.Position = 0;

        return new Bitmap(memStream);
    }

    private async Task<Bitmap?> GenerateVideoThumbnailAsync(string filePath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var options = new MediaOptions(MediaMode.Video);
                using var reader = DecoderRegistry.OpenMediaFile(filePath, options);
                if (reader == null || !reader.HasVideo)
                    return null;

                // 最初のフレームを読み取る
                if (!reader.ReadVideo(0, out var bmpRef) || bmpRef == null)
                    return null;

                using (bmpRef)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ToThumbnail(bmpRef.Value.SKBitmap, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to generate video thumbnail for {FilePath}", filePath);
                return null;
            }
        }, cancellationToken);
    }

    private async Task<Bitmap?> GenerateTemplateThumbnailAsync(string filePath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (ObjectTemplateService.Instance.TryLoadFromFile(filePath)?.Preview is not { Length: > 0 } preview)
                    return null;

                using var decoded = SKBitmap.Decode(preview);
                if (decoded == null)
                    return null;

                cancellationToken.ThrowIfCancellationRequested();
                return ToThumbnail(decoded, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read the template preview of {FilePath}", filePath);
                return null;
            }
        }, cancellationToken);
    }

    private void PruneCaches()
    {
        if (_disposed)
            return;

        PruneThumbnailCacheIfNeeded();
        EvictMediaInfoCacheIfNeeded();
    }

    // Rewriting a file keeps its path, so the timestamp is what separates a hit from a stale entry;
    // without it an overwritten template or image serves its old thumbnail for the whole session.
    private bool TryGetCached(string filePath, DateTime lastWriteUtc, out Bitmap? thumbnail)
    {
        if (_cache.TryGetValue(filePath, out CachedThumbnail entry)
            && entry.LastWriteUtc == lastWriteUtc
            && entry.Bitmap.TryGetTarget(out Bitmap? cached))
        {
            thumbnail = cached;
            return true;
        }

        thumbnail = null;
        return false;
    }

    private static DateTime GetLastWriteTimeOrDefault(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath);
        }
        catch
        {
            return default;
        }
    }

    private void PruneThumbnailCacheIfNeeded()
    {
        // 死んだ WeakReference エントリを除去
        foreach (var kvp in _cache)
        {
            if (!kvp.Value.Bitmap.TryGetTarget(out _))
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }

        // プルーニング後もサイズ上限を超えている場合、キャッシュをクリア
        if (_cache.Count > MaxThumbnailCacheEntries)
        {
            _cache.Clear();
        }
    }

    private void EvictMediaInfoCacheIfNeeded()
    {
        if (_mediaInfoCache.Count <= MaxMediaInfoCacheEntries)
            return;

        // LRU: 最終アクセスが古いエントリから削除して容量の75%まで減らす
        int targetCount = MaxMediaInfoCacheEntries * 3 / 4;
        var entriesToRemove = _mediaInfoCache
            .OrderBy(kvp => kvp.Value.LastAccessTicks)
            .Take(_mediaInfoCache.Count - targetCount)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in entriesToRemove)
        {
            _mediaInfoCache.TryRemove(key, out _);
        }
    }

    public bool CanGenerateThumbnail(string filePath)
    {
        return DecoderFileExtensions.Classify(filePath) is MediaFileKind.Image or MediaFileKind.Video
               || IsObjectTemplateFile(filePath);
    }

    public void ClearCache()
    {
        _cache.Clear();
        _mediaInfoCache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pruneTimer.Dispose();
        _cache.Clear();
        _mediaInfoCache.Clear();
        _semaphore.Dispose();
    }
}
