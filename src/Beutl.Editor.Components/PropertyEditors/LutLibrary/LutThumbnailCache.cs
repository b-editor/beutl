using Avalonia.Media.Imaging;
using Beutl.Controls;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

internal readonly record struct LutThumbnailResult(Bitmap? Image, string? Error);

internal interface ILutThumbnailSource
{
    Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken);
}

// The tiles of one LUT picker: each file's still, drawn as the effect picker draws a LutEffect's thumbnail, with the
// file as the effect's LUT, on the element the picker edits, else on the effect thumbnails' sample. The element is
// captured once, when the first tile asks, so the cache lives as long as one picker. At most a few drawings wait on
// the render thread at a time; one waiting for a slot is cancelled with its picker, and its entry is dropped so a
// later request retries it. Dispose drops every bitmap the cache still owns and, once no drawing reads it, the
// captured element. GetAsync is called on the UI thread, which the capture reads the live element on.
internal sealed class LutThumbnailCache : ILutThumbnailSource, IDisposable
{
    private const int DefaultConcurrency = 3;

    private readonly SemaphoreSlim _slots;
    private readonly Func<CancellationToken, Task<LibraryThumbnailSubject?>>? _capture;
    private readonly Func<string, LibraryThumbnailSubject?, CancellationToken, Task<LutThumbnailResult>> _load;
    private readonly Dictionary<(string Path, long Modified), Task<LutThumbnailResult>> _entries = [];
    private readonly List<Task> _runs = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private Task<LibraryThumbnailSubject?>? _subject;
    private bool _disposed;

    /// <param name="capture">
    /// Captures what the tiles show the LUTs on, or is <see langword="null"/> to show them on the sample.
    /// </param>
    public LutThumbnailCache(SceneRenderServices services, Func<CancellationToken, Task<LibraryThumbnailSubject?>>? capture)
        : this(DefaultConcurrency, capture, (path, subject, cancellationToken) => RenderAsync(services, path, subject, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    internal LutThumbnailCache(
        int maxConcurrent,
        Func<CancellationToken, Task<LibraryThumbnailSubject?>>? capture,
        Func<string, LibraryThumbnailSubject?, CancellationToken, Task<LutThumbnailResult>> load)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        _slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _capture = capture;
        _load = load;
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

            _subject ??= CaptureAsync();
            var completion = new TaskCompletionSource<LutThumbnailResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _entries.Add(key, completion.Task);
            Task run = RunAsync(path, key, _subject, cancellationToken, completion);
            _runs.Add(run);
            _ = run.ContinueWith(
                finished =>
                {
                    lock (_gate)
                    {
                        _runs.Remove(finished);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return completion.Task;
        }
    }

    // Content that cannot be captured leaves the tiles on the sample.
    private async Task<LibraryThumbnailSubject?> CaptureAsync()
    {
        if (_capture == null)
            return null;

        try
        {
            return await _capture(_disposeCancellation.Token);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task RunAsync(
        string path,
        (string Path, long Modified) key,
        Task<LibraryThumbnailSubject?> subject,
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
                LibraryThumbnailSubject? captured = await subject.ConfigureAwait(false);
                // A started drawing is not interrupted; only work still waiting for a slot is cancelled.
                LutThumbnailResult result = await Task.Run(
                    () => _load(path, captured, CancellationToken.None)).ConfigureAwait(false);
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

    // The file is read here only to tell an unreadable one apart: a LutEffect without a LUT it can read draws the
    // content unchanged, which would pass for an identity LUT.
    private static async Task<LutThumbnailResult> RenderAsync(
        SceneRenderServices services, string path, LibraryThumbnailSubject? subject, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            CubeFile.FromStream(stream);
        }
        catch (Exception)
        {
            return new LutThumbnailResult(null, Strings.LutLibraryInvalidFile);
        }

        var source = new CubeSource();
        source.ReadFrom(UriHelper.CreateFromPath(path));
        using LibraryThumbnailSheet? sheet = await LibraryThumbnailRenderer.RenderAsync(
            services,
            typeof(LutEffect),
            subject,
            stillOnly: true,
            effect => ((LutEffect)effect).Source.Value = source,
            cancellationToken).ConfigureAwait(false);
        return new LutThumbnailResult(sheet?.Bitmap.ToAvaWriteableBitmap(), null);
    }

    public void Dispose()
    {
        List<Task<LutThumbnailResult>> entries;
        Task[] runs;
        Task<LibraryThumbnailSubject?>? subject;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
            runs = [.. _runs];
            subject = _subject;
            _subject = null;
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

        if (subject != null)
            _ = DisposeSubjectWhenDoneAsync(subject, runs);
    }

    // A drawing may still read the captured element; it is disposed once every drawing has ended.
    private static async Task DisposeSubjectWhenDoneAsync(Task<LibraryThumbnailSubject?> subject, Task[] runs)
    {
        try
        {
            await Task.WhenAll(runs).ConfigureAwait(false);
        }
        catch
        {
            // A failed drawing still ended.
        }

        LibraryThumbnailSubject? captured = await subject.ConfigureAwait(false);
        captured?.Dispose();
    }
}
