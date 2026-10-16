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
    private readonly long _frameCount;
    private readonly Channel<Bitmap> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private readonly RetainedRenderTargetCheckpoint _retentionCheckpoint;
    private long _nextFrame;
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
        // Fixed at the start, so a duration edited during the export cannot end the frames early.
        _frameCount = ToFrameCount(scene.Duration, rate);
        _retentionCheckpoint = new RetainedRenderTargetCheckpoint(retainedRenderTargetReleaseInterval);

        // A buffered frame is an RgbaF16 snapshot at the frame size, so by default the buffer holds what fits the
        // read-ahead budget. An Output.FrameBufferSize preference still overrides it.
        int bufferSize = Preferences.Default.Get(
            "Output.FrameBufferSize",
            ReadAheadBudget.FrameCount(ReadAheadBudget.SnapshotBytes(renderer.FrameSize), MaxBufferedFrames));
        _channel = Channel.CreateBounded<Bitmap>(
            new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        _producerTask = Task.Run(RenderFramesAsync, _cts.Token);
    }

    public long FrameCount => _frameCount;

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
            for (long frame = 0; frame < _frameCount && !_cts.Token.IsCancellationRequested; frame++)
            {
                var bitmap = await RenderFrameCore(frame, _cts.Token);
                await _channel.Writer.WriteAsync(bitmap, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
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

    public async ValueTask<Bitmap?> ReadNextFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextFrame >= _frameCount)
            return null;

        _progress.OnNext(FrameToTime(_nextFrame));

        // WaitToReadAsync rethrows the producer's own error, which the export reports.
        while (await _channel.Reader.WaitToReadAsync(_cts.Token))
        {
            if (_channel.Reader.TryRead(out Bitmap? bitmap))
            {
                _nextFrame++;
                return bitmap;
            }
        }

        throw new InvalidOperationException($"Rendering stopped before frame {_nextFrame}.");
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
            }
        }

        while (_channel.Reader.TryRead(out Bitmap? bitmap))
        {
            bitmap.Dispose();
        }

        _cts.Dispose();
    }
}
