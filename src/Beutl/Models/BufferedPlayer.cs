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
    private readonly BufferedPlayerWaitGate _waitRenderGate;
    private readonly BufferedPlayerWaitGate _waitTimerGate;
    private readonly IDisposable _disposable;
    private int? _requestedFrame;
    private RenderFailure? _renderFailure;
    private volatile bool _isDisposed;

    // True once the producer loop stops. WaitRender re-checks it after publishing its token, so a producer that
    // stops while no one waits cannot block the next wait; ProducerStopped lets the consumer end playback early.
    private volatile bool _producerStopped;

    public bool ProducerStopped => _producerStopped;

    // The memory the queued frames may take; tests lower it to check the bound with small frames.
    internal long ReadAheadBytes { get; init; } = ReadAheadBudget.Bytes;

    internal RenderFailure? Failure => Volatile.Read(ref _renderFailure);

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
        _playbackToken = playbackToken;
        _waitRenderGate = new(() => _isDisposed || _producerStopped || _playbackToken.IsCancellationRequested || !_isPlaying.Value);
        _waitTimerGate = new(() => ShouldStopProducing);

        _disposable = isPlaying.Where(v => !v).Subscribe(_ =>
        {
            _waitRenderGate.Cancel();
            _waitTimerGate.Cancel();
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
            int frame = startFrame;
            SceneRenderer? activeRenderer = null;
            FrameCacheManager? activeCacheManager = null;
            try
            {
                if (IsCanceled)
                    return;

                _logger.LogInformation("Start rendering from frame {StartFrame} to {EndFrame}", startFrame, endFrame);
                for (; frame < endFrame; frame++)
                {
                    if (ShouldStopProducing)
                    {
                        _logger.LogInformation("Rendering stopped at frame {Frame}", frame);
                        break;
                    }

                    // A rendered frame is queued as a full-size snapshot (one from the frame cache is smaller), so
                    // the lead is bounded by memory rather than by a fixed frame count.
                    if (_queue.Count >= MaxQueuedFrames(_editViewModel.Renderer.Value))
                    {
                        WaitTimer();
                    }

                    if (ShouldStopProducing)
                    {
                        _logger.LogInformation("Rendering stopped at frame {Frame}", frame);
                        break;
                    }

                    // Re-read the pair each frame; a rebuild-by-replacement may have disposed the old instances.
                    activeRenderer = _editViewModel.Renderer.Value;
                    activeCacheManager = _editViewModel.FrameCacheManager.Value;
                    if (activeRenderer.IsDisposed || activeCacheManager.IsDisposed)
                    {
                        _logger.LogInformation(
                            "Renderer rebuilt mid-playback; stopping the playback producer at frame {Frame}", frame);
                        break;
                    }

                    TimeSpan time = TimeSpanExtensions.ToTimeSpan(frame, _rate);

                    if (!TryEnqueueFrame(activeRenderer, activeCacheManager, frame, time))
                        break;

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
                }

                _logger.LogInformation("Rendering completed.");
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
                // Record the stop before waking the waiters, so a consumer reaching WaitRender after this point
                // sees the stop through its post-publish re-check instead of waiting for a wakeup that never comes.
                _producerStopped = true;
                _waitRenderGate.Cancel();
                _waitTimerGate.Cancel();
                if (_isDisposed)
                {
                    while (_queue.TryDequeue(out Frame queuedFrame))
                    {
                        queuedFrame.Bitmap.Dispose();
                    }
                }
            }
        }, Threading.DispatchPriority.High);
    }

    private int MaxQueuedFrames(SceneRenderer renderer)
        => ReadAheadBudget.FrameCount(
            ReadAheadBudget.SnapshotBytes(renderer.DeviceSize), MaxReadAheadFrames, ReadAheadBytes);

    // Queues the frame from the cache, or renders and caches it. False when playback was canceled
    // part-way, which ends the producer loop.
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
        _waitTimerGate.Cancel();
        if (_queue.TryDequeue(out Frame f))
        {
            frame = f;
            return true;
        }

        WaitRender();

        return _queue.TryDequeue(out frame);
    }

    private void WaitRender()
    {
        if (_isDisposed || _producerStopped) return;
        using var cts = new CancellationTokenSource();
        if (!_waitRenderGate.Publish(cts)) return;

        cts.Token.WaitHandle.WaitOne();
        _waitRenderGate.Clear(cts);
    }

    private void WaitTimer()
    {
        if (_isDisposed) return;
        using var cts = new CancellationTokenSource();
        if (!_waitTimerGate.Publish(cts)) return;

        cts.Token.WaitHandle.WaitOne();
        _waitTimerGate.Clear(cts);
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
            _waitTimerGate.Cancel();
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
