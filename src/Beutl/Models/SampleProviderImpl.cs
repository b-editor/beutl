using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Audio.Composing;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;

namespace Beutl.Models;

public sealed class SampleProviderImpl : ISampleProvider, IDisposable
{
    private readonly Scene _scene;
    private readonly SceneComposer _composer;
    private readonly long _sampleRate;
    private readonly Subject<TimeSpan> _progress;
    private readonly Channel<(long Offset, Pcm<Stereo32BitFloat> Pcm)> _channel;
    private readonly ConcurrentDictionary<long, Pcm<Stereo32BitFloat>> _bufferedChunks = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _producerTask;
    private Pcm<Stereo32BitFloat>? _lastChunk;
    private long _lastOffset;
    private readonly int _chunkSize;
    private bool _disposed;

    public SampleProviderImpl(Scene scene, SceneComposer composer, long sampleRate, Subject<TimeSpan> progress)
    {
        _scene = scene;
        _composer = composer;
        _sampleRate = sampleRate;
        _progress = progress;
        _chunkSize = checked((int)sampleRate);

        _channel = Channel.CreateBounded<(long Offset, Pcm<Stereo32BitFloat> Pcm)>(
            new BoundedChannelOptions(3)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        _producerTask = Task.Run(ComposeSamplesAsync, _cts.Token);
    }

    public long SampleCount => (long)(_scene.Duration.TotalSeconds * _sampleRate);

    public long SampleRate => _sampleRate;

    public async ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int lengthInt = (int)length;
        var pcm = new Pcm<Stereo32BitFloat>((int)_sampleRate, lengthInt);
        int written = 0;

        while (written < lengthInt)
        {
            long currentOffset = offset + written;
            long chunkOffset = currentOffset - currentOffset % _chunkSize;
            Pcm<Stereo32BitFloat> chunk = await GetChunk(chunkOffset);

            if (_lastOffset != chunkOffset)
            {
                _lastChunk?.Dispose();
                _lastChunk = chunk;
                _lastOffset = chunkOffset;
            }
            else
            {
                _lastChunk = chunk;
            }

            int startInChunk = (int)(currentOffset - chunkOffset);
            var srcSpan = chunk.DataSpan[startInChunk..];
            int copyLength = Math.Min(lengthInt - written, srcSpan.Length);
            if (copyLength <= 0)
                break;

            srcSpan[..copyLength].CopyTo(pcm.DataSpan[written..]);
            written += copyLength;
        }

        return pcm;
    }

    private async ValueTask<Pcm<Stereo32BitFloat>> GetChunk(long chunkOffset)
    {
        if (_lastChunk != null && _lastOffset == chunkOffset)
        {
            return _lastChunk;
        }

        if (_bufferedChunks.TryRemove(chunkOffset, out var buffered))
        {
            return buffered;
        }

        while (await _channel.Reader.WaitToReadAsync(_cts.Token))
        {
            if (_channel.Reader.TryRead(out var item))
            {
                if (item.Offset == chunkOffset)
                {
                    return item.Pcm;
                }

                _bufferedChunks[item.Offset] = item.Pcm;
            }
        }

        throw new InvalidOperationException($"The requested chunk at offset {chunkOffset} could not be composed.");
    }

    private async Task ComposeSamplesAsync()
    {
        try
        {
            for (long offset = 0; offset < SampleCount && !_cts.Token.IsCancellationRequested; offset += _chunkSize)
            {
                var pcm = await ComposeChunk(offset, _cts.Token);
                await _channel.Writer.WriteAsync((offset, pcm), _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);
            return;
        }

        _channel.Writer.TryComplete();
    }

    private async ValueTask<Pcm<Stereo32BitFloat>> ComposeChunk(long offset, CancellationToken cancellationToken)
    {
        int length = (int)Math.Min(_chunkSize, SampleCount - offset);
        try
        {
            if (ComposeThread.Dispatcher.CheckAccess())
            {
                return ComposeCore(offset, length);
            }
            else
            {
                return await ComposeThread.Dispatcher.InvokeAsync(() => ComposeCore(offset, length), ct: cancellationToken);
            }
        }
        finally
        {
            _progress.OnNext(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * Math.Min(offset + length, SampleCount) / _sampleRate));
        }
    }

    private Pcm<Stereo32BitFloat> ComposeCore(long offset, int length)
    {
        var buffer = _composer.Compose(new(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * offset / _sampleRate) + _scene.Start,
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
                // ignore
            }
        }

        while (_channel.Reader.TryRead(out var item))
        {
            item.Pcm.Dispose();
        }

        foreach ((_, Pcm<Stereo32BitFloat> pcm) in _bufferedChunks)
        {
            pcm.Dispose();
        }

        _lastChunk?.Dispose();
        _cts.Dispose();
    }
}
