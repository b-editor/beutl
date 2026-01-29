using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Services;

/// <summary>
/// ファイルのサムネイル生成サービス
/// </summary>
public sealed class FileThumbnailService : IDisposable
{
    private static readonly Lazy<FileThumbnailService> s_instance = new(() => new FileThumbnailService());
    private readonly ConcurrentDictionary<string, WeakReference<Bitmap>> _cache = new();
    private readonly SemaphoreSlim _semaphore = new(4); // 同時生成数を制限
    private readonly ILogger _logger = Log.CreateLogger<FileThumbnailService>();
    private bool _disposed;

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

    /// <summary>
    /// 指定されたファイルのサムネイルを非同期で取得します
    /// </summary>
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

            using var resized = original.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.Medium);
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

                    using var resized = skBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.Medium);
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

    /// <summary>
    /// 指定されたファイルがサムネイル生成可能かどうかを判定します
    /// </summary>
    public bool CanGenerateThumbnail(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return s_imageExtensions.Contains(extension) || s_videoExtensions.Contains(extension);
    }

    /// <summary>
    /// キャッシュをクリアします
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cache.Clear();
        _semaphore.Dispose();
    }
}
