using System.Collections.Concurrent;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Models;

public sealed class BufferedPlayer : IPlayer
{
    private readonly ILogger _logger = Log.CreateLogger<BufferedPlayer>();
    private readonly ConcurrentQueue<IPlayer.Frame> _queue = new();
    private readonly EditViewModel _editViewModel;
    private readonly IEditorClock _editorClock;
    private readonly Scene _scene;
    private readonly IReadOnlyReactiveProperty<bool> _isPlaying;
    private readonly int _rate;
    private volatile CancellationTokenSource? _waitRenderToken;
    private volatile CancellationTokenSource? _waitTimerToken;
    private readonly IDisposable _disposable;
    private int? _requestedFrame;
    private bool _isDisposed;

    // Set true whenever the producer (Start's dispatched loop) is no longer running. The consumer waits on a
    // per-wait token that only the producer cancels, so a producer that stops while no one is waiting (e.g. a
    // mid-playback renderer rebuild) would leave the next WaitRender blocked forever. WaitRender re-checks this
    // flag after publishing its token; the producer sets it before cancelling — together that closes the race.
    private volatile bool _producerStopped;

    public BufferedPlayer(
        EditViewModel editViewModel, Scene scene,
        IReactiveProperty<bool> isPlaying, int rate)
    {
        _editViewModel = editViewModel;
        _editorClock = editViewModel.GetRequiredService<IEditorClock>();
        _scene = scene;
        _isPlaying = isPlaying;
        _rate = rate;

        _disposable = isPlaying.Where(v => !v).Subscribe(_ =>
        {
            _waitRenderToken?.Cancel();
            _waitTimerToken?.Cancel();
        });
    }

    public void Start()
    {
        int startFrame = (int)_editorClock.CurrentTime.Value.ToFrameNumber(_rate);
        int durationFrame = (int)Math.Ceiling(_scene.Duration.ToFrameNumber(_rate));

        RenderThread.Dispatcher.Dispatch(() =>
        {
            _producerStopped = false;
            try
            {
                _logger.LogInformation("Start rendering from frame {StartFrame} to {DurationFrame}", startFrame,
                    durationFrame);
                int endFrame = (int)_scene.Start.ToFrameNumber(_rate) + durationFrame;
                for (int frame = startFrame; frame < endFrame; frame++)
                {
                    if (!_isPlaying.Value)
                    {
                        _logger.LogInformation("Rendering stopped at frame {Frame}", frame);
                        break;
                    }

                    if (_queue.Count >= 120)
                    {
                        WaitTimer();
                    }

                    if (!_isPlaying.Value)
                    {
                        _logger.LogInformation("Rendering stopped at frame {Frame}", frame);
                        break;
                    }

                    // The renderer / frame-cache pair is rebuilt by replacement (and the old instances disposed)
                    // when the scene's frame size or preview scale changes, e.g. an undo/redo of a Scene Settings
                    // edit during playback. Re-read the current pair each frame rather than holding the ones
                    // captured at Start(), or Render()/Snapshot() runs on a disposed renderer and throws. If the
                    // read lands in the swap window and exposes a disposed instance, stop this producer; the
                    // finally below records the stop and wakes a waiting consumer so it does not block forever on a
                    // gone producer. The post-rebuild repaint redraws only the still preview
                    // (PlayerViewModel.QueueRender), not the playback queue, so playback halts on the last
                    // buffered frame until the user re-initiates it.
                    SceneRenderer renderer = _editViewModel.Renderer.Value;
                    FrameCacheManager frameCacheManager = _editViewModel.FrameCacheManager.Value;
                    if (renderer.IsDisposed || frameCacheManager.IsDisposed)
                    {
                        _logger.LogInformation(
                            "Renderer rebuilt mid-playback; stopping the playback producer at frame {Frame}", frame);
                        break;
                    }

                    TimeSpan time = TimeSpanExtensions.ToTimeSpan(frame, _rate);

                    // キャッシュを探す
                    // cacheは参照を既に追加されている
                    if (frameCacheManager.TryGet(frame, out Ref<Bitmap>? cache))
                    {
                        _queue.Enqueue(new(cache, frame));
                    }
                    else
                    {
                        var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
                        renderer.Render(compositionFrame);
                        using (Ref<Bitmap> bitmap = Ref<Bitmap>.Create(renderer.Snapshot()))
                        {
                            _queue.Enqueue(new(bitmap.Clone(), frame));
                            frameCacheManager.Add(frame, bitmap);
                        }
                    }

                    _waitRenderToken?.Cancel();
                    if (_isPlaying.Value)
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
            catch (ObjectDisposedException ex)
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
                NotificationService.ShowError(MessageStrings.UnexpectedError,
                    MessageStrings.FrameDrawingException);
                _logger.LogError(ex, "An exception occurred while drawing the frame.");
            }
            finally
            {
                // Whenever the producer thread exits — normal completion, mid-playback renderer rebuild, or an
                // exception — record the stop and wake any current waiter. The full fence pairs with WaitRender's
                // post-publish re-check so a consumer that reaches WaitRender after this point observes the stop
                // instead of blocking forever on a wakeup that will never come (the lost-wakeup hang).
                _producerStopped = true;
                Interlocked.MemoryBarrier();
                _waitRenderToken?.Cancel();
                _waitTimerToken?.Cancel();
            }
        }, Threading.DispatchPriority.High);
    }

    public bool TryDequeue(out IPlayer.Frame frame)
    {
        _waitTimerToken?.Cancel();
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
        _waitRenderToken = new CancellationTokenSource();

        // Re-check after publishing the token. The producer's finally sets _producerStopped then cancels the
        // current token; the full fence here pairs with the fence there so either the producer sees the token we
        // just published (and cancels it, waking us) or we see the stop flag (and bail). Without this, a producer
        // that stops while we are between publishing the token and WaitOne would leave us blocked forever.
        Interlocked.MemoryBarrier();
        if (_isDisposed || _producerStopped)
        {
            _waitRenderToken = null;
            return;
        }

        _waitRenderToken.Token.WaitHandle.WaitOne();
        _waitRenderToken = null;
    }

    private void WaitTimer()
    {
        if (_isDisposed) return;
        _waitTimerToken = new CancellationTokenSource();

        _waitTimerToken.Token.WaitHandle.WaitOne();
        _waitTimerToken = null;
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
            _waitRenderToken?.Cancel();
            _waitTimerToken?.Cancel();
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
}
