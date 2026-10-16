using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Audio.Composing;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Models;

public sealed class SampleProviderImpl : ISampleProvider, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<SampleProviderImpl>();
    private readonly Scene _scene;
    private readonly SceneComposer _composer;
    private readonly long _sampleRate;
    private readonly Subject<TimeSpan> _progress;
    private readonly long _sampleCount;
    private readonly Channel<Pcm<Stereo32BitFloat>> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private Pcm<Stereo32BitFloat>? _chunk;
    private int _chunkPosition;
    private long _nextSample;
    private readonly int _chunkSize;
    private bool _disposed;

    public SampleProviderImpl(Scene scene, SceneComposer composer, long sampleRate, Subject<TimeSpan> progress)
    {
        _scene = scene;
        _composer = composer;
        _sampleRate = sampleRate;
        _progress = progress;
        _chunkSize = (int)sampleRate;
        // Fixed at the start, so a duration edited during the export cannot end the samples early.
        _sampleCount = FrameProviderImpl.ToFrameCount(scene.Duration, new Rational(sampleRate));

        int bufferSize = Preferences.Default.Get("Output.SampleBufferSize", 3);
        _channel = Channel.CreateBounded<Pcm<Stereo32BitFloat>>(
            new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        _producerTask = Task.Run(ComposeSamplesAsync, _cts.Token);
    }

    public long SampleCount => _sampleCount;

    public long SampleRate => _sampleRate;

    public async ValueTask<Pcm<Stereo32BitFloat>?> ReadNextSamples(int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long offset = _nextSample;
        if (offset >= _sampleCount)
            return null;

        _progress.OnNext(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * Math.Min(offset + length, _sampleCount) / _sampleRate));

        // Past the end the samples stay zero.
        var pcm = new Pcm<Stereo32BitFloat>((int)_sampleRate, length);
        int available = (int)Math.Min(length, _sampleCount - offset);
        try
        {
            for (int written = 0; written < available;)
            {
                if (_chunk == null || _chunkPosition == _chunk.NumSamples)
                {
                    Pcm<Stereo32BitFloat> next = await ReadChunk();
                    _chunk?.Dispose();
                    _chunk = next;
                    _chunkPosition = 0;
                }

                int copyLength = Math.Min(available - written, _chunk.NumSamples - _chunkPosition);
                _chunk.DataSpan.Slice(_chunkPosition, copyLength).CopyTo(pcm.DataSpan[written..]);
                _chunkPosition += copyLength;
                written += copyLength;
            }
        }
        catch
        {
            pcm.Dispose();
            throw;
        }

        _nextSample = offset + length;
        return pcm;
    }

    private async ValueTask<Pcm<Stereo32BitFloat>> ReadChunk()
    {
        // WaitToReadAsync rethrows the producer's own error, which the export reports.
        while (await _channel.Reader.WaitToReadAsync(_cts.Token))
        {
            if (_channel.Reader.TryRead(out Pcm<Stereo32BitFloat>? chunk))
                return chunk;
        }

        throw new InvalidOperationException($"Composing stopped before sample {_nextSample}.");
    }

    private async Task ComposeSamplesAsync()
    {
        try
        {
            for (long offset = 0; offset < _sampleCount && !_cts.Token.IsCancellationRequested; offset += _chunkSize)
            {
                var pcm = await ComposeChunk(offset, _cts.Token);
                await _channel.Writer.WriteAsync(pcm, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while composing samples.");
            _channel.Writer.TryComplete(ex);
            return;
        }

        _logger.LogDebug("Sample composing completed.");
        _channel.Writer.TryComplete();
    }

    private async ValueTask<Pcm<Stereo32BitFloat>> ComposeChunk(long offset, CancellationToken cancellationToken)
    {
        int length = (int)Math.Min(_chunkSize, SampleCount - offset);

        if (ComposeThread.Dispatcher.CheckAccess())
        {
            return ComposeCore(offset, length);
        }
        else
        {
            return await ComposeThread.Dispatcher.InvokeAsync(() => ComposeCore(offset, length), ct: cancellationToken);
        }
    }

    private Pcm<Stereo32BitFloat> ComposeCore(long offset, int length)
    {
        using var buffer = _composer.Compose(new(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * offset / _sampleRate) + _scene.Start,
            TimeSpan.FromSeconds(1)))
                     ?? throw new InvalidOperationException("composer.Composeがnullを返しました。");
        var pcm = buffer.ToPcm();
        if (pcm.NumSamples != length)
        {
            var trimmed = new Pcm<Stereo32BitFloat>((int)_sampleRate, length);
            pcm.DataSpan[..length].CopyTo(trimmed.DataSpan);
            pcm.Dispose();
            pcm = trimmed;
        }

        return pcm;
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

        while (_channel.Reader.TryRead(out Pcm<Stereo32BitFloat>? chunk))
        {
            chunk.Dispose();
        }

        _chunk?.Dispose();
        _cts.Dispose();
    }
}
