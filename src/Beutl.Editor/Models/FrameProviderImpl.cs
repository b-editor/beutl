using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Models;

public sealed class FrameProviderImpl : IFrameProvider, IDisposable
{
    // The buffer size before the read-ahead budget, still the most frames an export renders ahead by default.
    private const int MaxBufferedFrames = 100;

    private readonly ILogger _logger = Log.CreateLogger<FrameProviderImpl>();
    private readonly Scene _scene;
    private readonly Rational _rate;
    private readonly SceneRenderer _renderer;
    private readonly Subject<TimeSpan> _progress;
    private readonly Channel<(long Frame, Bitmap Bitmap)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private readonly RetainedRenderTargetCheckpoint _retentionCheckpoint;
    private bool _disposed;

    public FrameProviderImpl(Scene scene, Rational rate, SceneRenderer renderer, Subject<TimeSpan> progress)
        : this(scene, rate, renderer, progress, RetainedRenderTargetCheckpoint.DefaultReleaseInterval)
    {
    }

    internal FrameProviderImpl(
        Scene scene,
        Rational rate,
        SceneRenderer renderer,
        Subject<TimeSpan> progress,
        int retainedRenderTargetReleaseInterval)
    {
        _scene = scene;
        _rate = rate;
        _renderer = renderer;
        _progress = progress;
        _retentionCheckpoint = new RetainedRenderTargetCheckpoint(retainedRenderTargetReleaseInterval);

        // A buffered frame is an RgbaF16 snapshot at the frame size, so by default the buffer holds what fits the
        // read-ahead budget. An Output.FrameBufferSize preference still overrides it.
        int bufferSize = Preferences.Default.Get(
            "Output.FrameBufferSize",
            ReadAheadBudget.FrameCount(ReadAheadBudget.SnapshotBytes(renderer.FrameSize), MaxBufferedFrames));
        _channel = Channel.CreateBounded<(long Frame, Bitmap Bitmap)>(
            new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        _producerTask = Task.Run(RenderFramesAsync, _cts.Token);
    }

    public long FrameCount => ToFrameCount(_scene.Duration, _rate);

    public Rational FrameRate => _rate;

    private Bitmap RenderCore(TimeSpan time)
    {
        var frame = _renderer.Compositor.EvaluateGraphics(time + _scene.Start);
        _renderer.Render(frame);
        Bitmap snapshot = _renderer.Snapshot();

        // Downscale supersampled render to FrameSize before encode (no-op when OutputScale == 1).
        Bitmap normalized = SupersampleDownscaler.ToFrameSize(snapshot, _renderer.FrameSize, _renderer.OutputScale);
        if (!ReferenceEquals(normalized, snapshot))
        {
            snapshot.Dispose();
        }

        // Encode buffer must match FrameSize exactly; verify at runtime so a regression fails loudly.
        if (normalized.Width != _renderer.FrameSize.Width || normalized.Height != _renderer.FrameSize.Height)
        {
            string actual = $"{normalized.Width}x{normalized.Height}";
            normalized.Dispose();
            throw new InvalidOperationException(
                $"Encode buffer {actual} must equal the output frame size {_renderer.FrameSize}; " +
                "SupersampleDownscaler failed to normalize the supersampled render to the output resolution.");
        }

        if (_retentionCheckpoint.Advance())
        {
            try
            {
                _renderer.ReleaseRetainedRenderTargets();
            }
            catch
            {
                normalized.Dispose();
                throw;
            }
        }

        return normalized;
    }

    // rate.Numerator, rate.Denominatorを使ってできるだけ正確に
    // (frame / (rate.Numerator / rate.Denominator)) * TimeSpan.TicksPerSecond
    private TimeSpan FrameToTime(long frame)
        => TimeSpan.FromTicks(frame * _rate.Denominator * TimeSpan.TicksPerSecond / _rate.Numerator);

    // The number of frames in duration, rounded to the nearest frame. Frame-aligned scene lengths are
    // truncated to whole ticks and fall just short of their frame count, so truncating here would drop
    // the last frame
    internal static long ToFrameCount(TimeSpan duration, Rational rate)
    {
        long numerator = rate.Numerator;
        long denominator = rate.Denominator;
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        if (duration <= TimeSpan.Zero || numerator <= 0 || denominator == 0)
            return 0;

        // duration.Ticks / (TicksPerSecond * denominator / numerator), with the frame length kept exact
        Int128 dividend = (Int128)duration.Ticks * numerator;
        Int128 divisor = (Int128)TimeSpan.TicksPerSecond * denominator;
        return (long)((2 * dividend + divisor) / (2 * divisor));
    }

    private async ValueTask<Bitmap> RenderFrameCore(long frame, CancellationToken cancellationToken)
    {
        var time = FrameToTime(frame);

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

        var time = FrameToTime(frame);
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
