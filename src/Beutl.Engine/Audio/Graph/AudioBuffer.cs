using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Beutl.Audio.Graph;

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

    public void Dispose()
    {
        if (!_disposed)
        {
            _memoryOwner.Dispose();
            _disposed = true;
        }
    }
}