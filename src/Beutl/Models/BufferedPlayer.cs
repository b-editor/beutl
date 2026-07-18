using System.Collections.Concurrent;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Models;

internal sealed class BufferedPlayer : IPlayer
{
    private readonly ILogger _logger = Log.CreateLogger<BufferedPlayer>();
    private readonly ConcurrentQueue<IPlayer.Frame> _queue = new();
    private readonly EditViewModel _editViewModel;
    private readonly IEditorClock _editorClock;
    private readonly Scene _scene;
    private readonly IReadOnlyReactiveProperty<bool> _isPlaying;
    private readonly CancellationToken _playbackToken;
    private readonly int _rate;
    private readonly BufferedPlayerWaitGate _waitRenderGate;
    private readonly BufferedPlayerWaitGate _waitTimerGate;
    private readonly IDisposable _disposable;
    private int? _requestedFrame;
    private RenderFailure? _renderFailure;
    private volatile bool _isDisposed;

    // Set true whenever the producer (Start's dispatched loop) is no longer running. The consumer waits on a
    // per-wait token that only the producer cancels, so a producer that stops while no one is waiting (e.g. a
    // mid-playback renderer rebuild) would leave the next WaitRender blocked forever. WaitRender re-checks this
    // flag after publishing its token; the producer sets it before cancelling — together that closes the race.
    // Exposed via IPlayer.ProducerStopped so the consumer timer can auto-stop playback on a premature exit.
    private volatile bool _producerStopped;

    public bool ProducerStopped => _producerStopped;

    internal RenderFailure? Failure => Volatile.Read(ref _renderFailure);

    public BufferedPlayer(
        EditViewModel editViewModel, Scene scene,
        IReactiveProperty<bool> isPlaying, int rate,
        CancellationToken playbackToken)
    {
        _editViewModel = editViewModel;
        _editorClock = editViewModel.GetRequiredService<IEditorClock>();
        _scene = scene;
        _isPlaying = isPlaying;
        _rate = rate;
        _playbackToken = playbackToken;
        _waitRenderGate = new(() => _isDisposed || _producerStopped);
        _waitTimerGate = new(() => _isDisposed);

        _disposable = isPlaying.Where(v => !v).Subscribe(_ =>
        {
            _waitRenderGate.Cancel();
            _waitTimerGate.Cancel();
        });
    }

    public void Start()
    {
        int startFrame = (int)_editorClock.CurrentTime.Value.ToFrameNumber(_rate);
        int durationFrame = (int)Math.Ceiling(_scene.Duration.ToFrameNumber(_rate));

        RenderThread.Dispatcher.Dispatch(() =>
        {
            Volatile.Write(ref _renderFailure, null);
            _producerStopped = false;
            int frame = startFrame;
            SceneRenderer? activeRenderer = null;
            FrameCacheManager? activeCacheManager = null;
            try
            {
                if (_isDisposed || _playbackToken.IsCancellationRequested)
                    return;

                _logger.LogInformation("Start rendering from frame {StartFrame} to {DurationFrame}", startFrame,
                    durationFrame);
                int endFrame = (int)_scene.Start.ToFrameNumber(_rate) + durationFrame;
                for (; frame < endFrame; frame++)
                {
                    if (_isDisposed || _playbackToken.IsCancellationRequested || !_isPlaying.Value)
                    {
                        _logger.LogInformation("Rendering stopped at frame {Frame}", frame);
                        break;
                    }

                    if (_queue.Count >= 120)
                    {
                        WaitTimer();
                    }

                    if (_isDisposed || _playbackToken.IsCancellationRequested || !_isPlaying.Value)
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

                    // キャッシュを探す
                    // cacheは参照を既に追加されている
                    if (activeCacheManager.TryGet(frame, out Ref<Bitmap>? cache))
                    {
                        if (_isDisposed || _playbackToken.IsCancellationRequested)
                        {
                            cache.Dispose();
                            break;
                        }

                        _queue.Enqueue(new(cache, frame));
                    }
                    else
                    {
                        var compositionFrame = activeRenderer.Compositor.EvaluateGraphics(time);
                        activeRenderer.Render(compositionFrame);
                        if (_isDisposed || _playbackToken.IsCancellationRequested)
                            break;

                        using (Ref<Bitmap> bitmap = Ref<Bitmap>.Create(activeRenderer.Snapshot()))
                        {
                            if (_isDisposed || _playbackToken.IsCancellationRequested)
                                break;

                            _queue.Enqueue(new(bitmap.Clone(), frame));
                            activeCacheManager.Add(frame, bitmap);
                        }
                    }

                    _waitRenderGate.Cancel();
                    if (!_isDisposed && !_playbackToken.IsCancellationRequested && _isPlaying.Value)
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
                // A concurrent rebuild disposed the renderer/cache between the IsDisposed re-check and
                // Render()/Snapshot() — a TOCTOU the per-frame re-read narrows but cannot close, since disposal
                // runs on the UI thread and the loop on the render thread. This is an expected mid-swap race, not
                // a drawing failure, so do NOT surface a user-facing FrameDrawingException; just log and let the
                // producer stop (the finally records the stop and wakes the consumer).
                _logger.LogWarning(ex, "Renderer disposed mid-frame by a concurrent rebuild; stopping the playback producer.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while drawing frame {Frame}.", frame);
                if (!_isDisposed && !_playbackToken.IsCancellationRequested && _isPlaying.Value)
                {
                    Volatile.Write(ref _renderFailure, new RenderFailure(ex, frame));
                }
            }
            finally
            {
                // Whenever the producer thread exits — normal completion, mid-playback renderer rebuild, or an
                // exception — record the stop before waking any current waiter: setting _producerStopped first
                // pairs with WaitRender's post-publish re-check (Cancel fences), so a consumer reaching WaitRender
                // after this point observes the stop instead of blocking forever on a wakeup that never comes.
                _producerStopped = true;
                _waitRenderGate.Cancel();
                _waitTimerGate.Cancel();
                if (_isDisposed)
                {
                    while (_queue.TryDequeue(out IPlayer.Frame queuedFrame))
                    {
                        queuedFrame.Bitmap.Dispose();
                    }
                }
            }
        }, Threading.DispatchPriority.High);
    }

    public bool TryDequeue(out IPlayer.Frame frame)
    {
        _waitTimerGate.Cancel();
        if (_queue.TryDequeue(out IPlayer.Frame f))
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
