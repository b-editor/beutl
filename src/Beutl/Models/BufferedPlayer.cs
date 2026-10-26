using System.Collections.Immutable;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Models;

internal sealed class BufferedPlayer : IDisposable
{
    public record struct Frame(Ref<Bitmap> Bitmap, int Time, Ref<Bitmap>? CleanBitmap = null) : IDisposable
    {
        public void Dispose()
        {
            Bitmap.Dispose();
            CleanBitmap?.Dispose();
        }
    }

    // The most frames playback renders ahead.
    private const int MaxReadAheadFrames = 120;

    // A parked producer waits for room in the queue, or at the end of the range for an edit to drop frames.
    private const int Running = 0, WaitingForRoom = 1, WaitingAtEnd = 2;

    private readonly ILogger _logger = Log.CreateLogger<BufferedPlayer>();
    // Locked with _editVersion and _restartFrame, since an edit drops frames from its tail.
    private readonly Queue<Frame> _queue = new();
    private readonly EditViewModel _editViewModel;
    private readonly Scene _scene;
    private readonly IReadOnlyReactiveProperty<bool> _isPlaying;
    private readonly CancellationToken _playbackToken;
    private readonly Rational _rate;
    // The consumer waits at most one frame for the producer, so a stalled render cannot hold the timer thread.
    private readonly TimeSpan _renderWait;
    private readonly BufferedPlayerWaitGate _waitRenderGate;
    private readonly IDisposable _disposable;
    private int? _requestedFrame;
    private RenderFailure? _renderFailure;
    private volatile bool _isDisposed;
    // The next frame to render and the end of the range; the render thread alone uses them.
    private int _frame;
    private int _endFrame;
    // Set while the producer waits with no job queued; whoever resets it to Running queues the next job.
    private int _parked;
    // A frame rendered across an edit is not cached; the producer goes on from the first frame an edit dropped.
    private int _editVersion;
    private int? _restartFrame;

    // True once the producer stops. WaitRender re-checks it after publishing its token, so a producer that
    // stops while no one waits cannot block the next wait; ProducerStopped lets the consumer end playback early.
    private volatile bool _producerStopped;

    public bool ProducerStopped => _producerStopped;

    // The memory the queued frames may take; tests lower it to check the bound with small frames.
    internal long ReadAheadBytes { get; init; } = ReadAheadBudget.Bytes;

    internal RenderFailure? Failure => Volatile.Read(ref _renderFailure);

    internal int QueuedFrames
    {
        get
        {
            lock (_queue)
                return _queue.Count;
        }
    }

    private bool IsCanceled => _isDisposed || _playbackToken.IsCancellationRequested;

    private bool ShouldStopProducing => IsCanceled || !_isPlaying.Value;

    public BufferedPlayer(
        EditViewModel editViewModel,
        IReactiveProperty<bool> isPlaying, Rational rate,
        CancellationToken playbackToken)
    {
        _editViewModel = editViewModel;
        _isPlaying = isPlaying;
        _rate = rate;
        _renderWait = TimeSpan.FromSeconds(1d / rate.ToDouble());
        _playbackToken = playbackToken;
        _waitRenderGate = new(() => _isDisposed || _producerStopped || _playbackToken.IsCancellationRequested || !_isPlaying.Value);

        _disposable = isPlaying.Where(v => !v).Subscribe(_ =>
        {
            _waitRenderGate.Cancel();
            Wake();
        });
        _scene = editViewModel.Scene;
        _scene.Edited += OnSceneEdited;
        _editViewModel.Player.CleanPreviewChanged += OnCleanPreviewChanged;
    }

    // The caller computes the range once, from the playhead it starts the ticker and the audio from.
    public void Start(PlaybackRange range)
    {
        (int startFrame, int endFrame) = range;

        RenderThread.Dispatcher.Dispatch(() =>
        {
            Volatile.Write(ref _renderFailure, null);
            _producerStopped = false;
            _frame = startFrame;
            _endFrame = endFrame;
            if (IsCanceled)
            {
                StopProducer();
                return;
            }

            _logger.LogInformation("Start rendering from frame {StartFrame} to {EndFrame}", startFrame, endFrame);
            RenderNextFrame();
        }, Threading.DispatchPriority.Medium);
    }

    // One frame per job, queued behind the jobs queued meanwhile, so a UI query waits for one frame at most.
    private void RenderNextFrame()
    {
        int frame;
        int editVersion;
        lock (_queue)
        {
            _frame = _restartFrame ?? _frame;
            _restartFrame = null;
            frame = _frame;
            editVersion = _editVersion;
        }

        SceneRenderer? activeRenderer = null;
        FrameCacheManager? activeCacheManager = null;
        bool stop = true;
        try
        {
            if (ShouldStopProducing)
            {
                _logger.LogInformation("Rendering stopped at frame {Frame}", frame);
                return;
            }

            // Re-read the pair each frame; a rebuild-by-replacement may have disposed the old instances.
            activeRenderer = _editViewModel.Renderer.Value;
            activeCacheManager = _editViewModel.FrameCacheManager.Value;
            if (activeRenderer.IsDisposed || activeCacheManager.IsDisposed)
            {
                _logger.LogInformation(
                    "Renderer rebuilt mid-playback; stopping the playback producer at frame {Frame}", frame);
                return;
            }

            // Playback ends by the clock; until then an edit may drop frames to draw again.
            if (frame >= _endFrame)
            {
                _logger.LogInformation("Rendering completed.");
                stop = false;
                Park(WaitingAtEnd);
                return;
            }

            // A rendered frame is queued as a full-size snapshot (one from the frame cache is smaller), so
            // the lead is bounded by memory rather than by a fixed frame count.
            if (QueuedFrames >= MaxQueuedFrames(activeRenderer))
            {
                stop = false;
                Park(WaitingForRoom);
                return;
            }

            MediaTime time = MediaTime.FromFrames(frame, _rate);

            EnqueueResult result = TryEnqueueFrame(activeRenderer, activeCacheManager, frame, time, editVersion);
            if (result == EnqueueResult.Canceled)
                return;

            stop = false;
            if (result == EnqueueResult.Dropped)
            {
                Schedule();
                return;
            }

            _waitRenderGate.Cancel();
            if (!ShouldStopProducing)
                _editViewModel.BufferStatus.EndTime.Value = time;

            int? requestedFrame = _requestedFrame;
            if (requestedFrame > frame)
            {
                _logger.LogDebug(
                    "Frame delay detected. Requested frame {RequestedFrame} is greater than current frame {Frame}",
                    requestedFrame, frame);
                frame = requestedFrame.Value + (requestedFrame.Value - frame) * 2;
                _requestedFrame = null;
            }

            _frame = frame + 1;
            Schedule();
        }
        catch (ObjectDisposedException ex) when (
            _isDisposed
            || activeRenderer?.IsDisposed == true
            || activeCacheManager?.IsDisposed == true)
        {
            // A concurrent rebuild disposed the renderer or cache mid-frame. That is not a drawing failure, so
            // log it and let the producer stop instead of surfacing a FrameDrawingException.
            _logger.LogWarning(ex, "Renderer disposed mid-frame by a concurrent rebuild; stopping the playback producer.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while drawing frame {Frame}.", frame);
            if (!ShouldStopProducing)
            {
                Volatile.Write(ref _renderFailure, new RenderFailure(ex, frame));
            }
        }
        finally
        {
            if (stop)
                StopProducer();
        }
    }

    private void Schedule() => RenderThread.Dispatcher.Dispatch(RenderNextFrame, Threading.DispatchPriority.Medium);

    // Waits without a job. Exchange fences against the wakers', so the re-check sees a wake that came before it.
    private void Park(int reason)
    {
        Interlocked.Exchange(ref _parked, reason);
        bool wake;
        lock (_queue)
        {
            wake = _restartFrame.HasValue
                || (reason == WaitingForRoom && _queue.Count < MaxQueuedFrames(_editViewModel.Renderer.Value));
        }

        if (wake || ShouldStopProducing)
            Wake();
    }

    // A taken frame wakes only a producer that waits for room.
    private void Wake(bool forRoom = false)
    {
        int parked = forRoom
            ? Interlocked.CompareExchange(ref _parked, Running, WaitingForRoom)
            : Interlocked.Exchange(ref _parked, Running);
        if (forRoom ? parked == WaitingForRoom : parked != Running)
            Schedule();
    }

    // Record the stop before waking the consumer, so a consumer reaching WaitRender after this point sees it
    // through its post-publish re-check instead of waiting for a wakeup that never comes.
    private void StopProducer()
    {
        _producerStopped = true;
        _waitRenderGate.Cancel();
        if (_isDisposed)
            DisposeQueuedFrames();
    }

    private void DisposeQueuedFrames()
    {
        Frame[] frames;
        lock (_queue)
        {
            frames = [.. _queue];
            _queue.Clear();
        }

        foreach (Frame frame in frames)
            frame.Dispose();
    }

    private void OnCleanPreviewChanged(object? sender, EventArgs e)
        => InvalidateQueuedFrames([(int.MinValue, int.MaxValue)], invalidateCache: false);

    // Drops the queued frames from the first one an edit changes, and their cache, so it shows on the next frame.
    private void OnSceneEdited(object? sender, EventArgs e)
    {
        // Without a range, as a replace or reset of the elements sends, the edit may change any frame.
        (int Start, int End)[] ranges = e is ElementEditedEventArgs { AffectedRange.IsDefaultOrEmpty: false } edited
            ? [.. WithTransitionRegions(edited.AffectedRange).Select(range => FrameCacheRanges.ToFrameRange(range, _rate))]
            : [(int.MinValue, int.MaxValue)];
        InvalidateQueuedFrames(ranges, invalidateCache: true);
    }

    private void InvalidateQueuedFrames((int Start, int End)[] ranges, bool invalidateCache)
    {
        if (_isDisposed || _producerStopped)
            return;

        var dropped = new List<Frame>();
        lock (_queue)
        {
            _editVersion++;
            for (int count = _queue.Count; count > 0; count--)
            {
                Frame queued = _queue.Dequeue();
                if (dropped.Count > 0 || ranges.Any(range => range.Start <= queued.Time && queued.Time < range.End))
                    dropped.Add(queued);
                else
                    _queue.Enqueue(queued);
            }

            if (dropped.Count > 0)
                _restartFrame = Math.Min(_restartFrame ?? int.MaxValue, dropped[0].Time);

            // Under the lock, so a frame job that sees the new count also finds these frames gone from the cache.
            FrameCacheManager cacheManager = _editViewModel.FrameCacheManager.Value;
            if (invalidateCache && !cacheManager.IsDisposed)
                cacheManager.DeleteAndUpdateBlocks(ranges);
        }

        foreach (Frame frame in dropped)
            frame.Dispose();

        if (dropped.Count > 0)
            Wake();
    }

    // A transition draws its elements past their own ranges, so the frames of one near an edit change too.
    private IEnumerable<TimeRange> WithTransitionRegions(ImmutableArray<TimeRange> ranges)
        => ranges.Concat(ranges.SelectMany(range => ElementTransitions.GetRegionsNear(_scene, range)));

    private int MaxQueuedFrames(SceneRenderer renderer)
        => ReadAheadBudget.FrameCount(
            ReadAheadBudget.SnapshotBytes(renderer.DeviceSize) * (_editViewModel.Player.IsCleanPreviewEnabled ? 2 : 1),
            MaxReadAheadFrames, ReadAheadBytes);

    // Queues the frame from the cache, or renders, queues and caches it. A frame rendered across an edit is shown
    // but not cached, so edits that come faster than frames render cannot hold playback.
    private EnqueueResult TryEnqueueFrame(
        SceneRenderer renderer, FrameCacheManager cacheManager, int frame, MediaTime time, int editVersion)
    {
        // cacheは参照を既に追加されている
        if (cacheManager.TryGet(frame, out Ref<Bitmap>? cache))
        {
            using (cache)
            {
                if (IsCanceled)
                    return EnqueueResult.Canceled;

                using var clean = _editViewModel.Player.CreatePlaybackCleanFrame(renderer, cache, time);
                return TryQueue(new(cache.Clone(), frame, clean?.Clone()));
            }
        }

        var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
        renderer.Render(compositionFrame);
        if (IsCanceled)
            return EnqueueResult.Canceled;

        using (Ref<Bitmap> bitmap = Ref<Bitmap>.Create(renderer.Snapshot()))
        {
            if (IsCanceled)
                return EnqueueResult.Canceled;

            using var clean = _editViewModel.Player.CreatePlaybackCleanFrame(renderer, bitmap);
            EnqueueResult result = TryQueue(new(bitmap.Clone(), frame, clean?.Clone()));
            // Outside the lock, which the consumer waits for. An edit since the frame was queued may have deleted
            // its cache before this Add, so the frame is taken back out.
            if (result == EnqueueResult.Queued && !EditedSince(editVersion))
            {
                cacheManager.Add(frame, bitmap);
                if (EditedSince(editVersion))
                    cacheManager.DeleteAndUpdateBlocks([(frame, frame + 1)]);
            }

            return result;
        }
    }

    // Takes the bitmap. A frame after the first one an edit dropped would come out of order, so it is drawn again.
    private EnqueueResult TryQueue(Frame frame)
    {
        lock (_queue)
        {
            if (!_restartFrame.HasValue)
            {
                _queue.Enqueue(frame);
                return EnqueueResult.Queued;
            }
        }

        frame.Dispose();
        return EnqueueResult.Dropped;
    }

    private bool EditedSince(int editVersion)
    {
        lock (_queue)
            return _editVersion != editVersion;
    }

    public bool TryDequeue(out Frame frame)
    {
        if (!TryTake(out frame))
        {
            WaitRender();
            if (!TryTake(out frame))
                return false;
        }

        // The taken frame makes room for the next one.
        Wake(forRoom: true);
        return true;
    }

    private bool TryTake(out Frame frame)
    {
        lock (_queue)
            return _queue.TryDequeue(out frame);
    }

    private void WaitRender()
    {
        if (_isDisposed || _producerStopped) return;
        using var cts = new CancellationTokenSource();
        if (!_waitRenderGate.Publish(cts)) return;

        cts.Token.WaitHandle.WaitOne(_renderWait);
        _waitRenderGate.Clear(cts);
    }

    public void Skipped(int requestedFrame)
    {
        _requestedFrame = requestedFrame;
    }

    public void Dispose()
    {
        try
        {
            _logger.LogInformation("Disposing BufferedPlayer.");

            _isDisposed = true;
            _scene.Edited -= OnSceneEdited;
            _editViewModel.Player.CleanPreviewChanged -= OnCleanPreviewChanged;
            _waitRenderGate.Cancel();
            // A parked producer runs once more to see the disposal and stop.
            Wake();
            _disposable.Dispose();
            DisposeQueuedFrames();

            _logger.LogInformation("BufferedPlayer disposed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while disposing.");
        }
    }

    internal sealed record RenderFailure(Exception Exception, int Frame);

    private enum EnqueueResult
    {
        Queued,
        Dropped,
        Canceled,
    }
}
