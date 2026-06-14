using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Configuration;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
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
    private readonly Channel<(long Frame, Bitmap Bitmap)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private bool _disposed;

    public FrameProviderImpl(Scene scene, Rational rate, SceneRenderer renderer, Subject<TimeSpan> progress)
    {
        _scene = scene;
        _rate = rate;
        _renderer = renderer;
        _progress = progress;

        int bufferSize = Preferences.Default.Get("Output.FrameBufferSize", 100);
        _channel = Channel.CreateBounded<(long Frame, Bitmap Bitmap)>(
            new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        _producerTask = Task.Run(RenderFramesAsync, _cts.Token);
    }

    public long FrameCount => (long)(_scene.Duration.TotalSeconds * _rate.ToDouble());

    public Rational FrameRate => _rate;

    private Bitmap RenderCore(TimeSpan time)
    {
        var frame = _renderer.Compositor.EvaluateGraphics(time + _scene.Start);
        _renderer.Render(frame);
        Bitmap snapshot = _renderer.Snapshot();

        // Export supersampling (feature 003, FR-026/FR-034): the renderer drew at ceil(FrameSize ×
        // RenderScale). Resample to EXACTLY FrameSize before encode so the delivered frame is always
        // output resolution. RenderScale == 1 returns the snapshot unchanged (byte-identical export).
        // Shared kernel via SupersampleDownscaler so export / still-frame / goldens never diverge.
        Bitmap normalized = SupersampleDownscaler.ToFrameSize(snapshot, _renderer.FrameSize, _renderer.RenderScale);
        if (!ReferenceEquals(normalized, snapshot))
        {
            snapshot.Dispose();
        }

        // feature 003 (FR-026): the buffer handed to the encoder MUST be exactly the output resolution, so a
        // scale change can never cause a stride/size mismatch. The downscale above guarantees this structurally;
        // enforce it at runtime (NOT a Debug.Assert, which is a no-op in Release export — exactly where the
        // encoder memcpy would silently corrupt on a stride mismatch). A future regression in
        // SupersampleDownscaler fails loudly here instead of producing a torn encoded frame.
        if (normalized.Width != _renderer.FrameSize.Width || normalized.Height != _renderer.FrameSize.Height)
        {
            string actual = $"{normalized.Width}x{normalized.Height}";
            normalized.Dispose();
            throw new InvalidOperationException(
                $"Encode buffer {actual} must equal the output frame size {_renderer.FrameSize} (FR-026); " +
                "SupersampleDownscaler failed to normalize the supersampled render to the output resolution.");
        }

        return normalized;
    }

    private async ValueTask<Bitmap> RenderFrameCore(long frame, CancellationToken cancellationToken)
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

    public async ValueTask<Bitmap> RenderFrame(long frame)
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
