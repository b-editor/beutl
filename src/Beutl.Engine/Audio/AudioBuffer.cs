using System.Buffers;

namespace Beutl.Audio;

public sealed class AudioBuffer : IDisposable
{
    private readonly IMemoryOwner<float> _memoryOwner;
    private readonly Memory<float> _memory;
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

        var totalSamples = channelCount * sampleCount;
        _memoryOwner = MemoryPool<float>.Shared.Rent(totalSamples);
        _memory = _memoryOwner.Memory.Slice(0, totalSamples);
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

        var start = channel * SampleCount;
        return _memory.Span.Slice(start, SampleCount);
    }

    public Memory<float> GetChannelMemory(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (channel < 0 || channel >= ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channel), $"Channel must be between 0 and {ChannelCount - 1}.");

        var start = channel * SampleCount;
        return _memory.Slice(start, SampleCount);
    }

    /// <summary>
    /// Returns the full channel-major backing span (channel <c>ch</c> sample <c>i</c> at index
    /// <c>ch * SampleCount + i</c>). Intended for hot DSP loops that would otherwise pay
    /// <see cref="GetChannelData"/>'s per-call overhead on every sample.
    /// </summary>
    internal Span<float> GetRawSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _memory.Span;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _memory.Span.Clear();
    }

    public void CopyTo(AudioBuffer destination)
    {
        ThrowIfIncompatibleDestination(destination);
        if (destination.SampleCount != SampleCount)
            throw new ArgumentException("Sample counts must match.", nameof(destination));

        _memory.CopyTo(destination._memory);
    }

    public void CopyTo(AudioBuffer destination, int offset)
    {
        ThrowIfIncompatibleDestination(destination);
        if (offset < 0 || offset + SampleCount > destination.SampleCount)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is out of range for the destination buffer.");

        for (int ch = 0; ch < ChannelCount; ch++)
        {
            var src = GetChannelData(ch);
            var dest = destination.GetChannelData(ch).Slice(offset, SampleCount);
            src.CopyTo(dest);
        }
    }

    public void CopyTo(AudioBuffer destination, int offset, int count)
    {
        ThrowIfIncompatibleDestination(destination);
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative.");
        if (count > SampleCount)
            throw new ArgumentOutOfRangeException(nameof(count), "Count exceeds source sample count.");
        if (offset < 0 || offset + count > destination.SampleCount)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is out of range for the destination buffer.");

        for (int ch = 0; ch < ChannelCount; ch++)
        {
            var src = GetChannelData(ch).Slice(0, count);
            var dest = destination.GetChannelData(ch).Slice(offset, count);
            src.CopyTo(dest);
        }
    }

    // Shared CopyTo preconditions: both buffers alive and the destination in the same format.
    private void ThrowIfIncompatibleDestination(AudioBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(destination._disposed, destination);

        if (destination.SampleRate != SampleRate)
            throw new ArgumentException("Sample rates must match.", nameof(destination));
        if (destination.ChannelCount != ChannelCount)
            throw new ArgumentException("Channel counts must match.", nameof(destination));
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
