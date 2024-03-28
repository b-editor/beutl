using System.Collections.Concurrent;

using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Services;
using Beutl.ViewModels;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace Beutl.Models;

public sealed class BufferedPlayer : IPlayer
{
    private readonly ILogger _logger = Log.CreateLogger<BufferedPlayer>();
    private readonly ConcurrentQueue<IPlayer.Frame> _queue = new();
    private readonly EditViewModel _editViewModel;
    private readonly FrameCacheManager _frameCacheManager;
    private readonly SceneRenderer _renderer;
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
        _frameCacheManager = editViewModel.FrameCacheManager.Value;
        _renderer = editViewModel.Renderer.Value;
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
        int startFrame = (int)_editViewModel.CurrentTime.Value.ToFrameNumber(_rate);
        int durationFrame = (int)Math.Ceiling(_scene.Duration.ToFrameNumber(_rate));

        RenderThread.Dispatcher.Dispatch(() =>
        {
            try
            {
                for (int frame = startFrame; frame < durationFrame; frame++)
                {
                    if (!_isPlaying.Value)
                        break;

                    if (_queue.Count >= 120)
                    {
                        // Debug.WriteLine("wait timer");
                        WaitTimer();
                    }

                    if (!_isPlaying.Value)
                        break;

                    TimeSpan time = TimeSpanExtensions.ToTimeSpan(frame, _rate);

                    // キャッシュを探す
                    // cacheは参照を既に追加されている
                    if (_frameCacheManager.TryGet(frame, out Ref<Bitmap<Bgra8888>>? cache))
                    {
                        _queue.Enqueue(new(cache, frame));
                    }
                    else
                    {
                        if (_renderer.Render(time))
                        {
                            // Debug.WriteLine($"{frame} rendered.");
                            using (Ref<Bitmap<Bgra8888>> bitmap = Ref<Bitmap<Bgra8888>>.Create(_renderer.Snapshot()))
                            {
                                _queue.Enqueue(new(bitmap.Clone(), frame));
                                _frameCacheManager.Add(frame, bitmap);
                            }
                        }
                        else
                        {
                            var blank = new Bitmap<Bgra8888>(_scene.FrameSize.Width, _scene.FrameSize.Height);
                            _queue.Enqueue(new(Ref<Bitmap<Bgra8888>>.Create(blank), frame));
                        }
                    }

                    _waitRenderToken?.Cancel();
                    if (_isPlaying.Value)
                        _editViewModel.BufferStatus.EndTime.Value = time;

                    int? requestedFrame = _requestedFrame;
                    if (requestedFrame > frame)
                    {
                        frame = requestedFrame.Value + 2;
                        _requestedFrame = null;
                    }
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred, Message.An_exception_occurred_while_drawing_frame);
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

        // Debug.WriteLine("wait rendered");
        WaitRender();

        return _queue.TryDequeue(out frame);
    }

    private void WaitRender()
    {
        if (_isDisposed) return;
        _waitRenderToken = new CancellationTokenSource();

        _waitRenderToken.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1d / _rate));
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
        // Debug.WriteLine($"{requestedFrame} skipped");
        _requestedFrame = requestedFrame;
    }

    public void Dispose()
    {
        _isDisposed = true;
        _waitRenderToken?.Cancel();
        _waitTimerToken?.Cancel();
        _disposable.Dispose();
        while (_queue.TryDequeue(out var f))
        {
            f.Bitmap.Dispose();
        }
    }
}
