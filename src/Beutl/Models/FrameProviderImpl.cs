using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Models;

public sealed class FrameProviderImpl : IFrameProvider, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<FrameProviderImpl>();
    private readonly Scene _scene;
    private readonly Rational _rate;
    private readonly SceneRenderer _renderer;
    private readonly Subject<TimeSpan> _progress;
    private readonly Channel<(long Frame, Bitmap<Bgra8888> Bitmap)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private bool _disposed;

    public FrameProviderImpl(Scene scene, Rational rate, SceneRenderer renderer, Subject<TimeSpan> progress)
    {
        _scene = scene;
        _rate = rate;
        _renderer = renderer;
        _progress = progress;

        _channel = Channel.CreateBounded<(long Frame, Bitmap<Bgra8888> Bitmap)>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        _producerTask = Task.Run(RenderFramesAsync, _cts.Token);
    }

    public long FrameCount => (long)(_scene.Duration.TotalSeconds * _rate.ToDouble());

    public Rational FrameRate => _rate;

    private Bitmap<Bgra8888> RenderCore(TimeSpan time)
    {
        int retry = 0;
        Retry:
        if (_renderer.Render(time + _scene.Start))
        {
            return _renderer.Snapshot();
        }

        if (retry > 3)
            throw new Exception("Renderer.RenderがFalseでした。他にこのシーンを使用していないか確認してください。");

        retry++;
        goto Retry;
    }

    private async ValueTask<Bitmap<Bgra8888>> RenderFrameCore(long frame, CancellationToken cancellationToken)
    {
        // rate.Numerator, rate.Denominatorを使ってできるだけ正確に
        // (frame / (rate.Numerator / rate.Denominator)) * TimeSpan.TicksPerSecond
        var time = TimeSpan.FromTicks(frame * _rate.Denominator * TimeSpan.TicksPerSecond / _rate.Numerator);

        if (RenderThread.Dispatcher.CheckAccess())
        {
            return RenderCore(time);
        }
        else
        {
            return await RenderThread.Dispatcher.InvokeAsync(() => RenderCore(time), ct: cancellationToken);
        }
    }

    private async Task RenderFramesAsync()
    {
        try
        {
            for (long frame = 0; frame < FrameCount && !_cts.Token.IsCancellationRequested; frame++)
            {
                var bitmap = await RenderFrameCore(frame, _cts.Token);
                await _channel.Writer.WriteAsync((frame, bitmap), _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while rendering frames.");
            _channel.Writer.TryComplete(ex);
            return;
        }

        _logger.LogDebug("Frame rendering completed.");
        _channel.Writer.TryComplete();
    }

    public async ValueTask<Bitmap<Bgra8888>> RenderFrame(long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var time = TimeSpan.FromTicks(frame * _rate.Denominator * TimeSpan.TicksPerSecond / _rate.Numerator);
        _progress.OnNext(time);

        while (await _channel.Reader.WaitToReadAsync(_cts.Token))
        {
            if (_channel.Reader.TryRead(out var item))
            {
                if (item.Frame == frame)
                {
                    return item.Bitmap;
                }

                item.Bitmap.Dispose();
                _logger.LogWarning("The frame is misaligned. Requested frame: {RequestedFrame}, Received frame: {ReceivedFrame}", frame, item.Frame);
                return await RenderFrameCore(frame, _cts.Token);
            }
        }

        _logger.LogWarning("The frame could not be read from the channel. Frame: {Frame}", frame);
        return await RenderFrameCore(frame, _cts.Token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();

        if (!_producerTask.IsCompleted)
        {
            try
            {
                _producerTask.Wait();
            }
            catch
            {
                // ignore
            }
        }

        while (_channel.Reader.TryRead(out var item))
        {
            item.Bitmap.Dispose();
        }

        _cts.Dispose();
    }
}
