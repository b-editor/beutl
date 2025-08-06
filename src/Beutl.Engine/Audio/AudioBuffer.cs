using System.Buffers;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio;

public sealed class AudioBuffer : IDisposable
{
    private readonly IMemoryOwner<float> _memoryOwner;
    private readonly Memory<float> _memory;
    private readonly int _channelSampleCount;
    private bool _disposed;

    public AudioBuffer(int sampleRate, int channelCount, int sampleCount)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (channelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCount), "Channel count must be positive.");
        if (sampleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be non-negative.");

        SampleRate = sampleRate;
        ChannelCount = channelCount;
        SampleCount = sampleCount;
        _channelSampleCount = sampleCount;

        var totalSamples = channelCount * sampleCount;
        _memoryOwner = MemoryPool<float>.Shared.Rent(totalSamples);
        _memory = _memoryOwner.Memory.Slice(0, totalSamples);

        // Clear the buffer
        _memory.Span.Clear();
    }

    public int SampleRate { get; }
    public int ChannelCount { get; }
    public int SampleCount { get; }

    public Span<float> GetChannelData(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (channel < 0 || channel >= ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channel), $"Channel must be between 0 and {ChannelCount - 1}.");

        var start = channel * _channelSampleCount;
        return _memory.Span.Slice(start, _channelSampleCount);
    }

    public Memory<float> GetChannelMemory(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (channel < 0 || channel >= ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channel), $"Channel must be between 0 and {ChannelCount - 1}.");

        var start = channel * _channelSampleCount;
        return _memory.Slice(start, _channelSampleCount);
    }

    public Pcm<Stereo32BitFloat> ToPcm()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ChannelCount != 2)
            throw new InvalidOperationException("AudioBuffer must have exactly 2 channels to convert to Pcm<Stereo32BitFloat>.");

        var pcm = new Pcm<Stereo32BitFloat>(SampleRate, SampleCount);
        for (int i = 0; i < SampleCount; i++)
        {
            pcm.DataSpan[i] = new Stereo32BitFloat(
                GetChannelData(0)[i],
                GetChannelData(1)[i]);
        }
        return pcm;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _memory.Span.Clear();
    }

    public void CopyTo(AudioBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(destination._disposed, destination);

        if (destination.SampleRate != SampleRate)
            throw new ArgumentException("Sample rates must match.", nameof(destination));
        if (destination.ChannelCount != ChannelCount)
            throw new ArgumentException("Channel counts must match.", nameof(destination));
        if (destination.SampleCount != SampleCount)
            throw new ArgumentException("Sample counts must match.", nameof(destination));

        _memory.CopyTo(destination._memory);
    }

    public void CopyTo(AudioBuffer destination, int offset)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(destination._disposed, destination);

        if (destination.SampleRate != SampleRate)
            throw new ArgumentException("Sample rates must match.", nameof(destination));
        if (destination.ChannelCount != ChannelCount)
            throw new ArgumentException("Channel counts must match.", nameof(destination));
        if (offset < 0 || offset + SampleCount > destination.SampleCount)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is out of range for the destination buffer.");

        for (int ch = 0; ch < ChannelCount; ch++)
        {
            var src = GetChannelData(ch);
            var dest = destination.GetChannelData(ch).Slice(offset, SampleCount);
            src.CopyTo(dest);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _memoryOwner.Dispose();
            _disposed = true;
        }
    }
}
