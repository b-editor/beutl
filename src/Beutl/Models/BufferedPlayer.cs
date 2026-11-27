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
    public record struct Frame(
        Ref<Bitmap> Bitmap, int Time, Ref<Bitmap>? CleanBitmap = null, string? PreviewWarning = null,
        PlayerViewModel.EffectComparisonFrame? Comparison = null) : IDisposable
    {
        public int Position { get; init; } = Time;

        public void Dispose()
        {
            Bitmap.Dispose();
            CleanBitmap?.Dispose();
            Comparison?.Dispose();
        }
    }

    // The most frames playback renders ahead.
    private const int MaxReadAheadFrames = 120;
    // Keep only a small lead while comparing so the next edited scene reaches the screen quickly.
    private const int MaxComparisonReadAheadFrames = 2;

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
    private readonly LoopPlayback? _loop;
    // The consumer waits at most one frame for the producer, so a stalled render cannot hold the timer thread.
    private readonly TimeSpan _renderWait;
    private readonly BufferedPlayerWaitGate _waitRenderGate;
    private readonly TaskCompletionSource? _firstFrameReady;
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

    public Task FirstFrameReady => _firstFrameReady?.Task ?? Task.CompletedTask;

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
        CancellationToken playbackToken, LoopPlayback? loop = null)
    {
        _editViewModel = editViewModel;
        _isPlaying = isPlaying;
        _rate = rate;
        _loop = loop;
        if (loop != null) _firstFrameReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        _editViewModel.Player.CleanPreviewChanged += OnPreviewFrameRequirementsChanged;
        _editViewModel.Player.EffectComparisonTargetChanged += OnPreviewFrameRequirementsChanged;
        if (_loop != null) _loop.PassAdded += OnPassAdded;
    }

    // The caller computes the range once, from the playhead it starts the ticker and the audio from.
    public void Start(PlaybackRange range)
    {
        PlayerViewModel player = _editViewModel.Player;
        if (player.ComparisonTarget is null)
        {
            StartProducing(range);
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // A paused edit outside the visible frame can matter as soon as playback reaches it.
            if (!ShouldStopProducing && player.ComparisonTarget is { } target)
                player.RefreshComparisonCapture(target);
            StartProducing(range);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void StartProducing(PlaybackRange range)
    {
        (int startFrame, int endFrame) = range;

        RenderThread.Dispatcher.Dispatch(() =>
        {
            Volatile.Write(ref _renderFailure, null);
            _producerStopped = false;
            _frame = _loop == null ? startFrame : 0;
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
        int position;
        int editVersion;
        lock (_queue)
        {
            _frame = _restartFrame ?? _frame;
            _restartFrame = null;
            position = _frame;
            editVersion = _editVersion;
        }

        int frame = position;
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

            // A looping producer waits here until the audio queue plans the next pass.
            LoopPlayback.Pass pass = default;
            if (_loop != null ? !_loop.TryGetPass(position, out pass) : position >= _endFrame)
            {
                _logger.LogInformation("Rendering completed.");
                stop = false;
                Park(WaitingAtEnd);
                return;
            }

            if (_loop != null) frame = pass.GetFrame(position);

            // A rendered frame is queued as a full-size snapshot (one from the frame cache is smaller), so
            // the lead is bounded by memory rather than by a fixed frame count.
            if (IsQueueFull(activeRenderer))
            {
                stop = false;
                Park(WaitingForRoom);
                return;
            }

            MediaTime time = MediaTime.FromFrames(frame, _rate);

            EnqueueResult result = TryEnqueueFrame(activeRenderer, activeCacheManager, frame, position, time, editVersion);
            if (result == EnqueueResult.Canceled)
                return;

            stop = false;
            if (result == EnqueueResult.Dropped)
            {
                Schedule();
                return;
            }

            _waitRenderGate.Cancel();
            if (position == 0) _firstFrameReady?.TrySetResult();
            if (!ShouldStopProducing)
            {
                if (_loop != null && position == pass.Position)
                    _editViewModel.BufferStatus.StartTime.Value = time;
                _editViewModel.BufferStatus.EndTime.Value = time;
            }

            int? requestedFrame = _requestedFrame;
            if (requestedFrame > position)
            {
                _logger.LogDebug(
                    "Frame delay detected. Requested frame {RequestedFrame} is greater than current frame {Frame}",
                    requestedFrame, position);
                int skipTo = requestedFrame.Value + (requestedFrame.Value - position) * 2;
                // Keep the first and last frames even if the producer has to skip inside a pass.
                position = _loop == null ? skipTo : Math.Max(position, Math.Min(skipTo, pass.EndPosition - 2));
                _requestedFrame = null;
            }

            _frame = position + 1;
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

    private void OnPassAdded() => Wake();

    // Waits without a job. Exchange fences against the wakers', so the re-check sees a wake that came before it.
    private void Park(int reason)
    {
        Interlocked.Exchange(ref _parked, reason);
        bool wake;
        lock (_queue)
        {
            wake = _restartFrame.HasValue
                || (reason == WaitingAtEnd && _loop?.TryGetPass(_frame, out _) == true)
                || (reason == WaitingForRoom && !IsQueueFull(_editViewModel.Renderer.Value));
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
        _firstFrameReady?.TrySetResult();
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

    private void OnPreviewFrameRequirementsChanged(object? sender, EventArgs e)
        => InvalidateQueuedFrames([(int.MinValue, int.MaxValue)], invalidateCache: false);

    // Comparison frames are already consistent. Keep the short queue and render subsequent pairs from the edit.
    private void OnSceneEdited(object? sender, EventArgs e)
    {
        if (_editViewModel.Player.ComparisonTarget is { } target)
            _editViewModel.Player.RequestPlaybackComparisonCapture(target);

        // Without a range, as a replace or reset of the elements sends, the edit may change any frame.
        (int Start, int End)[] ranges = e is ElementEditedEventArgs { AffectedRange.IsDefaultOrEmpty: false } edited
            ? [.. WithTransitionRegions(edited.AffectedRange).Select(range => FrameCacheRanges.ToFrameRange(range, _rate))]
            : [(int.MinValue, int.MaxValue)];
        InvalidateQueuedFrames(ranges, invalidateCache: true, keepComparisonFrames: true);
    }

    private void InvalidateQueuedFrames((int Start, int End)[] ranges, bool invalidateCache, bool keepComparisonFrames = false)
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
                if (dropped.Count > 0 || (queued.Comparison is null || !keepComparisonFrames)
                    && ranges.Any(range => range.Start <= queued.Time && queued.Time < range.End))
                    dropped.Add(queued);
                else
                    _queue.Enqueue(queued);
            }

            if (dropped.Count > 0)
                _restartFrame = Math.Min(_restartFrame ?? int.MaxValue, dropped[0].Position);

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
        => ranges.Concat(ElementTransitions.GetRegionsNear(_scene, ranges));

    // Leave room for the next frame and any separate clean image. A comparison keeps a shorter lead.
    private bool IsQueueFull(SceneRenderer renderer)
    {
        lock (_queue)
        {
            if (_editViewModel.Player.ComparisonTarget is not null)
                return _queue.Count >= MaxComparisonReadAheadFrames;
            if (_queue.Count >= MaxReadAheadFrames)
                return true;
            if (_queue.Count < ReadAheadBudget.MinFrames)
                return false;

            int snapshots = _editViewModel.Player.IsCleanPreviewEnabled ? 2 : 1;
            foreach (Frame frame in _queue)
            {
                snapshots += frame.CleanBitmap is { } clean && !ReferenceEquals(clean.Value, frame.Bitmap.Value) ? 2 : 1;
                if (frame.Comparison is not null)
                    snapshots++;
            }

            return snapshots * ReadAheadBudget.SnapshotBytes(renderer.DeviceSize) > ReadAheadBytes;
        }
    }

    // Queues the frame from the cache, or renders, queues and caches it. A frame rendered across an edit is shown
    // but not cached, so edits that come faster than frames render cannot hold playback.
    private EnqueueResult TryEnqueueFrame(
        SceneRenderer renderer, FrameCacheManager cacheManager, int frame, int position, MediaTime time, int editVersion)
    {
        if (_editViewModel.Player.ComparisonTarget is { } comparison)
            return TryEnqueueComparisonFrame(renderer, frame, position, time, comparison);

        // cacheは参照を既に追加されている
        if (cacheManager.TryGet(frame, out Ref<Bitmap>? cache, out string? warning))
        {
            using (cache)
            {
                if (IsCanceled)
                    return EnqueueResult.Canceled;

                using var clean = _editViewModel.Player.CreatePlaybackCleanFrame(renderer, cache, time);
                return TryQueue(new Frame(cache.Clone(), frame, clean?.Clone(), warning) { Position = position });
            }
        }

        var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
        warning = renderer.RenderWithWarning(compositionFrame);
        if (IsCanceled)
            return EnqueueResult.Canceled;

        using (Ref<Bitmap> bitmap = Ref<Bitmap>.Create(renderer.Snapshot()))
        {
            if (IsCanceled)
                return EnqueueResult.Canceled;

            using var clean = _editViewModel.Player.CreatePlaybackCleanFrame(renderer, bitmap);
            EnqueueResult result = TryQueue(new Frame(bitmap.Clone(), frame, clean?.Clone(), warning) { Position = position });
            // Outside the lock, which the consumer waits for. An edit since the frame was queued may have deleted
            // its cache before this Add, so the frame is taken back out.
            if (result == EnqueueResult.Queued && !EditedSince(editVersion))
            {
                cacheManager.Add(frame, bitmap, warning);
                if (EditedSince(editVersion))
                    cacheManager.DeleteAndUpdateBlocks([(frame, frame + 1)]);
            }

            return result;
        }
    }

    private EnqueueResult TryEnqueueComparisonFrame(
        SceneRenderer preview, int frame, int position, MediaTime time, PlayerViewModel.EffectComparisonTarget target)
    {
        // Hold one captured scene for both draws. An edit can make a newer capture available while this pair runs.
        PlayerViewModel player = _editViewModel.Player;
        var scene = player.GetComparisonRenderScene(preview, target.Capture);
        SceneRenderer renderer = scene.Renderer;
        using var comparison = PlayerViewModel.RenderComparisonBefore(scene, time);
        if (IsCanceled)
            return EnqueueResult.Canceled;

        string? warning = renderer.RenderWithWarning(renderer.Compositor.EvaluateGraphics(time));
        if (IsCanceled)
            return EnqueueResult.Canceled;

        using var bitmap = Ref<Bitmap>.Create(renderer.Snapshot());
        using var clean = player.CreatePlaybackCleanFrame(renderer, bitmap);
        return TryQueue(new Frame(bitmap.Clone(), frame, clean?.Clone(), warning,
            new(comparison.Target, comparison.Before.Clone()))
        {
            Position = position
        });
    }

    // Takes the bitmap. A frame after the first one an edit dropped would come out of order, so it is drawn again.
    private EnqueueResult TryQueue(Frame frame)
    {
        lock (_queue)
        {
            if (!_restartFrame.HasValue
                && ReferenceEquals(frame.Comparison?.Target, _editViewModel.Player.ComparisonTarget))
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

    public bool TryDequeue(out Frame frame) => TryDequeue(int.MaxValue, out frame);

    public bool TryDequeue(int maxPosition, out Frame frame)
    {
        if (!TryTake(maxPosition, out frame, out bool waitingForRender))
        {
            if (!waitingForRender) return false;
            WaitRender();
            if (!TryTake(maxPosition, out frame, out _))
                return false;
        }

        // The taken frame makes room for the next one.
        Wake(forRoom: true);
        return true;
    }

    private bool TryTake(int maxPosition, out Frame frame, out bool waitingForRender)
    {
        lock (_queue)
        {
            waitingForRender = !_queue.TryPeek(out Frame next);
            if (!waitingForRender && next.Position <= maxPosition)
            {
                frame = _queue.Dequeue();
                return true;
            }

            frame = default;
            return false;
        }
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
            _editViewModel.Player.CleanPreviewChanged -= OnPreviewFrameRequirementsChanged;
            _editViewModel.Player.EffectComparisonTargetChanged -= OnPreviewFrameRequirementsChanged;
            if (_loop != null) _loop.PassAdded -= OnPassAdded;
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
