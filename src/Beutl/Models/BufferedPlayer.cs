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

                    // The renderer / frame-cache pair is rebuilt by replacement when the scene's frame size
                    // (or preview scale) changes — e.g. an undo/redo of a Scene Settings edit while playback is
                    // running. The replaced instances are disposed, so the loop re-reads the current pair each
                    // frame instead of holding the ones it captured at Start(); otherwise it would call
                    // Render()/Snapshot() on a disposed renderer and throw ObjectDisposedException. When the read
                    // lands in the brief swap window and exposes a disposed instance, stop this producer — and
                    // FIRST cancel the wait token so the consumer's WaitRender() returns instead of blocking the
                    // UI thread on a producer that is gone. The repaint after a rebuild redraws only the still
                    // preview (PlayerViewModel.QueueRender), NOT the playback queue, so playback halts on the last
                    // buffered frame until the user re-initiates it — it does not seamlessly resume.
                    SceneRenderer renderer = _editViewModel.Renderer.Value;
                    FrameCacheManager frameCacheManager = _editViewModel.FrameCacheManager.Value;
                    if (renderer.IsDisposed || frameCacheManager.IsDisposed)
                    {
                        _logger.LogInformation(
                            "Renderer rebuilt mid-playback; stopping the playback producer at frame {Frame}", frame);
                        _waitRenderToken?.Cancel();
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
                // The renderer/cache was disposed by a concurrent rebuild between the IsDisposed re-check and
                // Render()/Snapshot() — a tight TOCTOU the per-frame re-read narrows but cannot fully close (the
                // disposal runs on the UI thread, the loop on the render thread). This is an EXPECTED mid-swap
                // race, not a real drawing failure, so do NOT surface a user-facing FrameDrawingException; cancel
                // the wait token so the consumer unblocks, log it, and let the producer stop.
                _waitRenderToken?.Cancel();
                _logger.LogWarning(ex, "Renderer disposed mid-frame by a concurrent rebuild; stopping the playback producer.");
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(MessageStrings.UnexpectedError,
                    MessageStrings.FrameDrawingException);
                _logger.LogError(ex, "An exception occurred while drawing the frame.");
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
        if (_isDisposed) return;
        _waitRenderToken = new CancellationTokenSource();

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
