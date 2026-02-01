using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Services;

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
    private const int MaxThumbnailCacheEntries = 1000;
    private const int MaxMediaInfoCacheEntries = 500;
    private static readonly TimeSpan s_pruneInterval = TimeSpan.FromSeconds(60);

    private static readonly Lazy<FileThumbnailService> s_instance = new(() => new FileThumbnailService());
    private readonly ConcurrentDictionary<string, WeakReference<Bitmap>> _cache = new();
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

    private static readonly HashSet<string> s_imageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
    ];

    private static readonly HashSet<string> s_videoExtensions =
    [
        ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".flv", ".webm"
    ];

    private static readonly HashSet<string> s_audioExtensions =
    [
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a"
    ];

    public async Task<Bitmap?> GetThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        // キャッシュを確認
        if (_cache.TryGetValue(filePath, out var weakRef) && weakRef.TryGetTarget(out var cached))
        {
            return cached;
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // 再度キャッシュを確認（競合回避）
            if (_cache.TryGetValue(filePath, out weakRef) && weakRef.TryGetTarget(out cached))
            {
                return cached;
            }

            Bitmap? thumbnail = null;

            if (s_imageExtensions.Contains(extension))
            {
                thumbnail = await GenerateImageThumbnailAsync(filePath, cancellationToken);
            }
            else if (s_videoExtensions.Contains(extension))
            {
                thumbnail = await GenerateVideoThumbnailAsync(filePath, cancellationToken);
            }

            if (thumbnail != null)
            {
                PruneThumbnailCacheIfNeeded();
                _cache[filePath] = new WeakReference<Bitmap>(thumbnail);
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

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        bool isVideo = s_videoExtensions.Contains(extension);
        bool isAudio = s_audioExtensions.Contains(extension);

        if (!isVideo && !isAudio)
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
                    var mode = isVideo ? MediaMode.Video : MediaMode.Audio;
                    var options = new MediaOptions(mode);
                    using var reader = DecoderRegistry.OpenMediaFile(filePath, options);
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

    public bool CanGetMediaInfo(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return s_videoExtensions.Contains(extension) || s_audioExtensions.Contains(extension);
    }

    public bool IsMediaFile(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return s_imageExtensions.Contains(extension)
            || s_videoExtensions.Contains(extension)
            || s_audioExtensions.Contains(extension);
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

            // アスペクト比を維持してリサイズ
            float scale = Math.Min((float)ThumbnailSize / original.Width, (float)ThumbnailSize / original.Height);
            int newWidth = (int)(original.Width * scale);
            int newHeight = (int)(original.Height * scale);

            using var resized = original.Resize(new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized == null)
                return null;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);

            cancellationToken.ThrowIfCancellationRequested();

            using var memStream = new MemoryStream();
            data.SaveTo(memStream);
            memStream.Position = 0;

            return new Bitmap(memStream);
        }, cancellationToken);
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
                if (!reader.ReadVideo(0, out var bmp) || bmp == null)
                    return null;

                using (bmp)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // SKBitmapに変換
                    using var skBitmap = bmp.ToSKBitmap();

                    // サムネイルサイズにリサイズ
                    float scale = Math.Min((float)ThumbnailSize / skBitmap.Width, (float)ThumbnailSize / skBitmap.Height);
                    int newWidth = (int)(skBitmap.Width * scale);
                    int newHeight = (int)(skBitmap.Height * scale);

                    using var resized = skBitmap.Resize(new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    if (resized == null)
                        return null;

                    using var image = SKImage.FromBitmap(resized);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 90);

                    using var memStream = new MemoryStream();
                    data.SaveTo(memStream);
                    memStream.Position = 0;

                    return new Bitmap(memStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to generate video thumbnail for {FilePath}", filePath);
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

    private void PruneThumbnailCacheIfNeeded()
    {
        // 死んだ WeakReference エントリを除去
        foreach (var kvp in _cache)
        {
            if (!kvp.Value.TryGetTarget(out _))
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
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return s_imageExtensions.Contains(extension) || s_videoExtensions.Contains(extension);
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
