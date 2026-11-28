using Avalonia.Media.Imaging;
using Beutl.Graphics;
using SkiaSharp;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

internal readonly record struct LutThumbnailResult(Bitmap? Image, string? Error);

internal interface ILutThumbnailSource
{
    Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken);
}

// Renders a thumbnail once per file version and keeps it for the editor's lifetime. At most a few renders run at
// a time; a render waiting for a slot is cancelled with its picker, and its entry is dropped so a later open
// retries it. Dispose drops every bitmap the cache still owns.
internal sealed class LutThumbnailCache : ILutThumbnailSource, IDisposable
{
    private const int DefaultConcurrency = 3;

    private readonly SemaphoreSlim _slots;
    private readonly Func<string, LutThumbnailResult> _load;
    private readonly Dictionary<(string Path, long Modified), Task<LutThumbnailResult>> _entries = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;

    public LutThumbnailCache()
        : this(DefaultConcurrency, null)
    {
    }

    internal LutThumbnailCache(int maxConcurrent, Func<string, LutThumbnailResult>? load)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        _slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _load = load ?? LoadFromFile;
    }

    internal int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed)
            return Task.FromResult(new LutThumbnailResult(null, null));
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<LutThumbnailResult>(cancellationToken);

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
            if (_disposed)
                return Task.FromResult(new LutThumbnailResult(null, null));
            if (_entries.TryGetValue(key, out Task<LutThumbnailResult>? existing))
                return existing;

            var completion = new TaskCompletionSource<LutThumbnailResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _entries.Add(key, completion.Task);
            _ = RunAsync(path, key, cancellationToken, completion);
            return completion.Task;
        }
    }

    private async Task RunAsync(
        string path,
        (string Path, long Modified) key,
        CancellationToken requestCancellation,
        TaskCompletionSource<LutThumbnailResult> completion)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation, _disposeCancellation.Token);
        try
        {
            await _slots.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                // A started render is not interrupted; only work still waiting for a slot is cancelled.
                LutThumbnailResult result = await Task.Run(() => _load(path)).ConfigureAwait(false);
                if (_disposed)
                {
                    result.Image?.Dispose();
                    result = new LutThumbnailResult(null, null);
                }

                completion.TrySetResult(result);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            RemoveEntry(key, completion.Task);
            completion.TrySetCanceled(linked.Token);
        }
        catch (Exception exception)
        {
            RemoveEntry(key, completion.Task);
            completion.TrySetResult(new LutThumbnailResult(null, exception.Message));
        }
    }

    // Only removes the entry this run owns: a newer request for the file may have replaced it.
    private void RemoveEntry((string Path, long Modified) key, Task task)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Task<LutThumbnailResult>? current)
                && ReferenceEquals(current, task))
            {
                _entries.Remove(key);
            }
        }
    }

    private static LutThumbnailResult LoadFromFile(string path)
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

        _disposeCancellation.Cancel();

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
