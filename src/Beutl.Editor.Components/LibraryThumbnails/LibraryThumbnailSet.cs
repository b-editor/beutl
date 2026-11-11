using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Editor.Components.LibraryThumbnails;

/// <summary>
/// The moving thumbnails of library types on one piece of the project: the frame the preview shows, or the object or
/// edge a picker adds to.
/// </summary>
/// <remarks>
/// The content is captured when a thumbnail first needs it, and again after <see cref="Refresh"/>. A still is drawn
/// for each thumbnail on screen and every frame for each one playing, one thumbnail at a time, playing ones first.
/// The frames of the few played most recently are kept for playing again. Used on the UI thread, but for
/// <see cref="GetSource"/>.
/// </remarks>
internal sealed class LibraryThumbnailSet : IDisposable
{
    // The frames of a thumbnail take 11 to 15 MB, so only a few that are not playing stay drawn.
    internal const int MaxIdleSheets = 4;

    private readonly ILogger _logger = Log.CreateLogger<LibraryThumbnailSet>();
    private readonly Func<CancellationToken, Task<LibraryThumbnailSubject?>>? _capture;
    private readonly Func<Type, LibraryThumbnailSubject?, bool, CancellationToken, Task<LibraryThumbnailSheet?>> _render;
    private readonly Dictionary<Type, LibraryThumbnailSource> _sources = [];
    private readonly List<LibraryThumbnailSource> _queue = [];
    // Least recently played first.
    private readonly List<LibraryThumbnailSource> _idleSheets = [];
    // Content captured before a refresh, removed once no drawing reads it.
    private readonly List<Task<LibraryThumbnailSubject?>> _retired = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<IReadOnlyList<Type>> _onRecipesChanged;
    private readonly Action<Type[]> _onTypesUnloading;
    private Task<LibraryThumbnailSubject?>? _subject;
    private Task _running = Task.CompletedTask;
    private bool _isRunning;
    private bool _disposed;

    /// <param name="capture">
    /// Captures the content the thumbnails show types on, or is <see langword="null"/> to show them on the samples.
    /// </param>
    public LibraryThumbnailSet(Func<CancellationToken, Task<LibraryThumbnailSubject?>>? capture)
        : this(capture, LibraryThumbnailRenderer.RenderAsync)
    {
    }

    internal LibraryThumbnailSet(
        Func<CancellationToken, Task<LibraryThumbnailSubject?>>? capture,
        Func<Type, LibraryThumbnailSubject?, bool, CancellationToken, Task<LibraryThumbnailSheet?>> render)
    {
        _capture = capture;
        _render = render;
        _onRecipesChanged = types => Dispatcher.UIThread.Post(() => Redraw(types));
        _onTypesUnloading = types => Dispatcher.UIThread.Post(() => Release(types));
        LibraryThumbnailRecipes.Changed += _onRecipesChanged;
        TypeUnloadNotifier.TypesUnloading += _onTypesUnloading;
    }

    // Completes once nothing is left to draw, for tests.
    internal Task Idle => _running;

    /// <summary>
    /// The thumbnail of <paramref name="type"/>, or <see langword="null"/> for a type that has none: a type that is
    /// neither a filter effect nor a transition, or one that cannot be created.
    /// </summary>
    public IAnimatedThumbnailSource? GetSource(Type type)
    {
        // A picker builds its items off the UI thread too.
        lock (_sources)
        {
            if (_disposed)
                return null;

            if (_sources.TryGetValue(type, out LibraryThumbnailSource? source))
                return source;

            if (LibraryThumbnailRenderer.GetLayout(type) is not { } layout)
                return null;

            source = new LibraryThumbnailSource(this, type, layout);
            _sources.Add(type, source);
            return source;
        }
    }

    /// <summary>
    /// Captures the content again, for a preview that has changed. Each thumbnail keeps what it shows until it is
    /// drawn again, which happens as soon as it is on screen.
    /// </summary>
    public void Refresh()
    {
        if (_disposed || _capture == null)
            return;

        if (_subject != null)
        {
            _retired.Add(_subject);
            _subject = null;
        }

        foreach (LibraryThumbnailSource source in Sources())
        {
            source.MarkStale();
            if (source.WantsStill || source.WantsFrames)
                Schedule(source);
        }
    }

    internal void Schedule(LibraryThumbnailSource source)
    {
        if (_disposed)
            return;

        if (!_queue.Contains(source))
            _queue.Add(source);

        if (!_isRunning)
        {
            _isRunning = true;
            _running = RunQueueAsync();
        }
    }

    internal void OnSheetLoaded(LibraryThumbnailSource source)
    {
        _idleSheets.Remove(source);
        if (!source.IsPlayed)
            KeepIdle(source);
    }

    internal void OnStoppedPlaying(LibraryThumbnailSource source)
    {
        if (source.HasSheet)
            KeepIdle(source);
    }

    private void KeepIdle(LibraryThumbnailSource source)
    {
        _idleSheets.Remove(source);
        _idleSheets.Add(source);
        while (_idleSheets.Count > MaxIdleSheets)
        {
            LibraryThumbnailSource oldest = _idleSheets[0];
            _idleSheets.RemoveAt(0);
            if (!oldest.IsPlayed)
                oldest.DropSheet();
        }
    }

    private LibraryThumbnailSource[] Sources()
    {
        lock (_sources)
        {
            return [.. _sources.Values];
        }
    }

    private async Task RunQueueAsync()
    {
        try
        {
            while (!_disposed && TakeNext() is { } source)
            {
                // Between drawings nothing reads content captured before a refresh.
                RemoveRetired();
                await LoadAsync(source);
            }
        }
        finally
        {
            _isRunning = false;
            RemoveRetired();
        }
    }

    // The thumbnails being played first; one no longer asked for leaves the queue.
    private LibraryThumbnailSource? TakeNext()
    {
        _queue.RemoveAll(source => !source.WantsStill && !source.WantsFrames);
        LibraryThumbnailSource? next = _queue.FirstOrDefault(source => source.WantsFrames) ?? _queue.FirstOrDefault();
        if (next != null)
            _queue.Remove(next);

        return next;
    }

    private async Task LoadAsync(LibraryThumbnailSource source)
    {
        Type type = source.Type;
        int generation = source.Generation;
        bool withFrames = source.WantsFrames;
        CancellationToken cancellationToken = _lifetime.Token;
        try
        {
            LibraryThumbnailSubject? subject = await GetSubjectAsync();
            LibraryThumbnailSheet? sheet = await _render(type, subject, !withFrames, cancellationToken);
            if (sheet == null)
            {
                source.SetFailed(generation);
            }
            else
            {
                (Bitmap still, Bitmap? frames) = await Task.Run(() =>
                {
                    using (sheet)
                    {
                        return ToImages(sheet, withFrames);
                    }
                });
                source.SetLoaded(generation, still, frames, sheet.Layout);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not draw the library thumbnail of {Type}.", type);
            source.SetFailed(generation);
        }

        // Playing can start, or the content change, while a drawing is under way.
        if (source.WantsStill || source.WantsFrames)
            Schedule(source);
    }

    private Task<LibraryThumbnailSubject?> GetSubjectAsync()
    {
        return _subject ??= CaptureAsync();
    }

    // Content that cannot be captured leaves the thumbnails on the samples.
    private async Task<LibraryThumbnailSubject?> CaptureAsync()
    {
        if (_capture == null)
            return null;

        try
        {
            return await _capture(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not capture the content for the library thumbnails.");
            return null;
        }
    }

    // A still drawn on its own is the whole sheet; otherwise the still is the sheet's frame shown at rest.
    private static (Bitmap Still, Bitmap? Frames) ToImages(LibraryThumbnailSheet sheet, bool withFrames)
    {
        if (!withFrames)
            return (sheet.Bitmap.ToAvaWriteableBitmap(), null);

        using BtlBitmap still = sheet.Bitmap.ExtractSubset(sheet.Layout.GetFrameRect(sheet.Layout.StillIndex));
        return (still.ToAvaWriteableBitmap(), sheet.Bitmap.ToAvaWriteableBitmap());
    }

    private void RemoveRetired()
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            Task<LibraryThumbnailSubject?> subject = _retired[i];
            if (!subject.IsCompleted)
                continue;

            _retired.RemoveAt(i);
            if (subject.IsCompletedSuccessfully)
                subject.Result?.Dispose();
        }
    }

    private void Redraw(IReadOnlyList<Type> types)
    {
        if (_disposed)
            return;

        foreach (Type type in types)
        {
            LibraryThumbnailSource? source;
            lock (_sources)
            {
                _sources.TryGetValue(type, out source);
            }

            if (source == null)
                continue;

            source.MarkStale();
            if (source.WantsStill || source.WantsFrames)
                Schedule(source);
        }
    }

    private void Release(Type[] types)
    {
        foreach (Type type in types)
        {
            LibraryThumbnailSource? source;
            lock (_sources)
            {
                _sources.Remove(type, out source);
            }

            if (source == null)
                continue;

            _idleSheets.Remove(source);
            _queue.Remove(source);
            source.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sources)
        {
            _disposed = true;
        }

        LibraryThumbnailRecipes.Changed -= _onRecipesChanged;
        TypeUnloadNotifier.TypesUnloading -= _onTypesUnloading;
        _lifetime.Cancel();
        foreach (LibraryThumbnailSource source in Sources())
        {
            source.Release();
        }

        lock (_sources)
        {
            _sources.Clear();
        }

        _queue.Clear();
        _idleSheets.Clear();
        if (_subject != null)
            _retired.Add(_subject);

        _subject = null;
        // A drawing may still read the content; it is removed once that drawing ends.
        _ = RemoveRetiredWhenDoneAsync();
    }

    private async Task RemoveRetiredWhenDoneAsync()
    {
        try
        {
            await _running;
            await Task.WhenAll(_retired.ToArray());
        }
        catch
        {
            // A failed capture left nothing to remove.
        }

        RemoveRetired();
    }
}
