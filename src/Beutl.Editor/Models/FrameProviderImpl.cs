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
    private readonly Subject<MediaTime> _progress;
    private readonly long _frameCount;
    private readonly Channel<Bitmap> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private readonly RetainedRenderTargetCheckpoint _retentionCheckpoint;
    private long _nextFrame;
    private bool _disposed;

    public FrameProviderImpl(Scene scene, Rational rate, SceneRenderer renderer, Subject<MediaTime> progress)
        : this(scene, rate, renderer, progress, RetainedRenderTargetCheckpoint.DefaultReleaseInterval)
    {
    }

    internal FrameProviderImpl(
        Scene scene,
        Rational rate,
        SceneRenderer renderer,
        Subject<MediaTime> progress,
        int retainedRenderTargetReleaseInterval)
    {
        _scene = scene;
        _rate = rate;
        _renderer = renderer;
        _progress = progress;
        // Fixed at the start, so a duration edited during the export cannot end the frames early.
        _frameCount = ToFrameCount(scene.Duration.Value, rate);
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

    private Bitmap RenderCore(MediaTime time, CancellationToken cancellationToken)
    {
        var frame = _renderer.Compositor.EvaluateGraphics(_scene.FromWindowTime(time), cancellationToken, _rate);
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

    private MediaTime FrameToTime(long frame) => MediaTime.FromFrames(frame, _rate);

    // The number of frames in duration, rounded to the nearest frame. A scene length written by hand can fall
    // just short of its frame count, so truncating here would drop the last frame
    internal static long ToFrameCount(MediaTime duration, Rational rate)
    {
        bool positiveRate = rate.Numerator != 0 && rate.Denominator != 0
            && long.IsNegative(rate.Numerator) == long.IsNegative(rate.Denominator);
        if (duration <= MediaTime.Zero || !positiveRate)
            return 0;

        return duration.ToFrame(rate, MidpointRounding.AwayFromZero);
    }

    private async ValueTask<Bitmap> RenderFrameCore(long frame, CancellationToken cancellationToken)
    {
        var time = FrameToTime(frame);

        if (RenderThread.Dispatcher.CheckAccess())
        {
            return RenderCore(time, cancellationToken);
        }
        else
        {
            return await RenderThread.Dispatcher.InvokeAsync(() => RenderCore(time, cancellationToken), ct: cancellationToken);
        }
    }

    private async Task RenderFramesAsync()
    {
        try
        {
            for (long frame = 0; frame < _frameCount && !_cts.Token.IsCancellationRequested; frame++)
            {
                var bitmap = await RenderFrameCore(frame, _cts.Token);
                await WriteFrameAsync(_channel.Writer, bitmap, _cts.Token);
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

    // Ownership passes to the channel only after the write succeeds. Cancellation can arrive while
    // a completed GPU readback is waiting for an encoder to drain the bounded channel.
    internal static async ValueTask WriteFrameAsync(ChannelWriter<Bitmap> writer, Bitmap bitmap, CancellationToken token)
    {
        try
        {
            await writer.WriteAsync(bitmap, token);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
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
