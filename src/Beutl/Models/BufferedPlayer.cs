using System.Collections.Concurrent;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Models;

internal sealed class BufferedPlayer : IDisposable
{
    public record struct Frame(Ref<Bitmap> Bitmap, int Time);

    // The most frames playback renders ahead.
    private const int MaxReadAheadFrames = 120;

    private readonly ILogger _logger = Log.CreateLogger<BufferedPlayer>();
    private readonly ConcurrentQueue<Frame> _queue = new();
    private readonly EditViewModel _editViewModel;
    private readonly IReadOnlyReactiveProperty<bool> _isPlaying;
    private readonly CancellationToken _playbackToken;
    private readonly int _rate;
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
    // 1 while the producer waits for room with no job queued; whoever resets it to 0 queues the next job.
    private int _parked;

    // True once the producer stops. WaitRender re-checks it after publishing its token, so a producer that
    // stops while no one waits cannot block the next wait; ProducerStopped lets the consumer end playback early.
    private volatile bool _producerStopped;

    public bool ProducerStopped => _producerStopped;

    // The memory the queued frames may take; tests lower it to check the bound with small frames.
    internal long ReadAheadBytes { get; init; } = ReadAheadBudget.Bytes;

    internal RenderFailure? Failure => Volatile.Read(ref _renderFailure);

    internal int QueuedFrames => _queue.Count;

    private bool IsCanceled => _isDisposed || _playbackToken.IsCancellationRequested;

    private bool ShouldStopProducing => IsCanceled || !_isPlaying.Value;

    public BufferedPlayer(
        EditViewModel editViewModel,
        IReactiveProperty<bool> isPlaying, int rate,
        CancellationToken playbackToken)
    {
        _editViewModel = editViewModel;
        _isPlaying = isPlaying;
        _rate = rate;
        _renderWait = TimeSpan.FromSeconds(1d / rate);
        _playbackToken = playbackToken;
        _waitRenderGate = new(() => _isDisposed || _producerStopped || _playbackToken.IsCancellationRequested || !_isPlaying.Value);

        _disposable = isPlaying.Where(v => !v).Subscribe(_ =>
        {
            _waitRenderGate.Cancel();
            Wake();
        });
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
        int frame = _frame;
        SceneRenderer? activeRenderer = null;
        FrameCacheManager? activeCacheManager = null;
        bool stop = true;
        try
        {
            if (frame >= _endFrame)
            {
                _logger.LogInformation("Rendering completed.");
                return;
            }

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

            // A rendered frame is queued as a full-size snapshot (one from the frame cache is smaller), so
            // the lead is bounded by memory rather than by a fixed frame count.
            if (QueuedFrames >= MaxQueuedFrames(activeRenderer))
            {
                stop = false;
                Park();
                return;
            }

            TimeSpan time = TimeSpanExtensions.ToTimeSpan(frame, _rate);

            if (!TryEnqueueFrame(activeRenderer, activeCacheManager, frame, time))
                return;

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
            stop = false;
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

    // Waits without a job until a frame is taken. Exchange fences against Wake, so the re-check sees an earlier one.
    private void Park()
    {
        Interlocked.Exchange(ref _parked, 1);
        if (ShouldStopProducing || QueuedFrames < MaxQueuedFrames(_editViewModel.Renderer.Value))
            Wake();
    }

    private void Wake()
    {
        if (Interlocked.Exchange(ref _parked, 0) == 1)
            Schedule();
    }

    // Record the stop before waking the consumer, so a consumer reaching WaitRender after this point sees it
    // through its post-publish re-check instead of waiting for a wakeup that never comes.
    private void StopProducer()
    {
        _producerStopped = true;
        _waitRenderGate.Cancel();
        if (_isDisposed)
        {
            while (_queue.TryDequeue(out Frame queuedFrame))
            {
                queuedFrame.Bitmap.Dispose();
            }
        }
    }

    private int MaxQueuedFrames(SceneRenderer renderer)
        => ReadAheadBudget.FrameCount(
            ReadAheadBudget.SnapshotBytes(renderer.DeviceSize), MaxReadAheadFrames, ReadAheadBytes);

    // Queues the frame from the cache, or renders and caches it. False when playback was canceled
    // part-way, which stops the producer.
    private bool TryEnqueueFrame(SceneRenderer renderer, FrameCacheManager cacheManager, int frame, TimeSpan time)
    {
        // cacheは参照を既に追加されている
        if (cacheManager.TryGet(frame, out Ref<Bitmap>? cache))
        {
            if (IsCanceled)
            {
                cache.Dispose();
                return false;
            }

            _queue.Enqueue(new(cache, frame));
        }
        else
        {
            var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
            renderer.Render(compositionFrame);
            if (IsCanceled)
                return false;

            using (Ref<Bitmap> bitmap = Ref<Bitmap>.Create(renderer.Snapshot()))
            {
                if (IsCanceled)
                    return false;

                _queue.Enqueue(new(bitmap.Clone(), frame));
                cacheManager.Add(frame, bitmap);
            }
        }

        return true;
    }

    public bool TryDequeue(out Frame frame)
    {
        if (!_queue.TryDequeue(out frame))
        {
            WaitRender();
            if (!_queue.TryDequeue(out frame))
                return false;
        }

        // The taken frame makes room for the next one.
        Wake();
        return true;
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
            _waitRenderGate.Cancel();
            // A parked producer runs once more to see the disposal and stop.
            Wake();
            _disposable.Dispose();
            while (_queue.TryDequeue(out var f))
            {
                f.Bitmap.Dispose();
            }

            _logger.LogInformation("BufferedPlayer disposed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while disposing.");
        }
    }

    internal sealed record RenderFailure(Exception Exception, int Frame);
}
