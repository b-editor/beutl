using Avalonia.Media.Imaging;
using Beutl.Graphics;
using SkiaSharp;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

internal readonly record struct LutThumbnailResult(Bitmap? Image, string? Error);

internal interface ILutThumbnailSource
{
    Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken);
}

// Renders a thumbnail once per file version and keeps it for the editor's lifetime. Generation and decoding
// happen off the UI thread; Dispose drops every bitmap the cache still owns.
internal sealed class LutThumbnailCache : ILutThumbnailSource, IDisposable
{
    private readonly Dictionary<(string Path, long Modified), Task<LutThumbnailResult>> _entries = [];
    private readonly object _gate = new();
    private bool _disposed;

    public Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed)
            return Task.FromResult(new LutThumbnailResult(null, null));

        long modified;
        try
        {
            modified = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception)
        {
            return Task.FromResult(new LutThumbnailResult(null, Strings.LutLibraryInvalidFile));
        }

        var key = (path, modified);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Task<LutThumbnailResult>? existing))
                return existing;

            Task<LutThumbnailResult> created = Task.Run(() => Load(path));
            _entries.Add(key, created);
            return created;
        }
    }

    private static LutThumbnailResult Load(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            CubeFile cube = CubeFile.FromStream(stream);
            byte[] pixels = LutThumbnailRenderer.CreateImage(cube);
            return new LutThumbnailResult(CreateBitmap(pixels), null);
        }
        catch (Exception)
        {
            return new LutThumbnailResult(null, Strings.LutLibraryInvalidFile);
        }
    }

    private static Bitmap CreateBitmap(byte[] pixels)
    {
        var info = new SKImageInfo(
            LutThumbnailRenderer.Width,
            LutThumbnailRenderer.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        using var skBitmap = new SKBitmap(info);
        pixels.AsSpan().CopyTo(skBitmap.GetPixelSpan());
        using SKImage image = SKImage.FromBitmap(skBitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using Stream stream = data.AsStream();
        return new Bitmap(stream);
    }

    public void Dispose()
    {
        List<Task<LutThumbnailResult>> entries;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
        }

        foreach (Task<LutThumbnailResult> entry in entries)
        {
            if (entry.IsCompletedSuccessfully)
            {
                entry.Result.Image?.Dispose();
            }
            else
            {
                entry.ContinueWith(
                    static task =>
                    {
                        if (task.IsCompletedSuccessfully)
                            task.Result.Image?.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}
