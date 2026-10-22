using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Models;

public sealed class SampleProviderImpl : ISampleProvider, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<SampleProviderImpl>();
    private readonly Scene _scene;
    private readonly SceneComposer _composer;
    private readonly long _sampleRate;
    private readonly Subject<MediaTime> _progress;
    private readonly long _sampleCount;
    private readonly Channel<AudioBuffer> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private AudioBuffer? _chunk;
    private int _chunkPosition;
    private long _nextSample;
    private readonly int _chunkSize;
    private bool _disposed;

    public SampleProviderImpl(Scene scene, SceneComposer composer, long sampleRate, Subject<MediaTime> progress)
    {
        _scene = scene;
        _composer = composer;
        _sampleRate = sampleRate;
        _progress = progress;
        _chunkSize = (int)sampleRate;
        // Fixed at the start, so a duration edited during the export cannot end the samples early.
        _sampleCount = FrameProviderImpl.ToFrameCount(scene.Duration, new Rational(sampleRate));

        int bufferSize = Preferences.Default.Get("Output.SampleBufferSize", 3);
        _channel = Channel.CreateBounded<AudioBuffer>(
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

    public async ValueTask<AudioBuffer?> ReadNextSamples(int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long offset = _nextSample;
        if (offset >= _sampleCount)
            return null;

        _progress.OnNext(MediaTime.FromSamples(Math.Min(offset + length, _sampleCount), (int)_sampleRate));

        // Past the end the samples stay zero.
        var audio = new AudioBuffer((int)_sampleRate, 2, length);
        int available = (int)Math.Min(length, _sampleCount - offset);
        try
        {
            for (int written = 0; written < available;)
            {
                if (_chunk == null || _chunkPosition == _chunk.SampleCount)
                {
                    AudioBuffer next = await ReadChunk();
                    _chunk?.Dispose();
                    _chunk = next;
                    _chunkPosition = 0;
                }

                int copyLength = Math.Min(available - written, _chunk.SampleCount - _chunkPosition);
                for (int channel = 0; channel < 2; channel++)
                {
                    _chunk.GetChannelData(channel).Slice(_chunkPosition, copyLength)
                        .CopyTo(audio.GetChannelData(channel)[written..]);
                }

                _chunkPosition += copyLength;
                written += copyLength;
            }
        }
        catch
        {
            audio.Dispose();
            throw;
        }

        _nextSample = offset + length;
        return audio;
    }

    private async ValueTask<AudioBuffer> ReadChunk()
    {
        // WaitToReadAsync rethrows the producer's own error, which the export reports.
        while (await _channel.Reader.WaitToReadAsync(_cts.Token))
        {
            if (_channel.Reader.TryRead(out AudioBuffer? chunk))
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
                var chunk = await ComposeChunk(offset, _cts.Token);
                await _channel.Writer.WriteAsync(chunk, _cts.Token);
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

    private async ValueTask<AudioBuffer> ComposeChunk(long offset, CancellationToken cancellationToken)
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

    private AudioBuffer ComposeCore(long offset, int length)
    {
        AudioBuffer buffer = _composer.Compose(new(MediaTime.FromSamples(offset, (int)_sampleRate) + _scene.Start,
            MediaTime.FromSeconds(1)))
                     ?? throw new InvalidOperationException("composer.Composeがnullを返しました。");
        if (buffer.SampleCount == length)
            return buffer;

        using (buffer)
        {
            var trimmed = new AudioBuffer(buffer.SampleRate, buffer.ChannelCount, length);
            buffer.CopyTo(trimmed, 0, length);
            return trimmed;
        }
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

        while (_channel.Reader.TryRead(out AudioBuffer? chunk))
        {
            chunk.Dispose();
        }

        _chunk?.Dispose();
        _cts.Dispose();
    }
}
