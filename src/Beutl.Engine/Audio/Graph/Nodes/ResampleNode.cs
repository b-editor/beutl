using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Beutl.Audio.Graph.Nodes;

public sealed class ResampleNode : AudioNode
{
    private int _sourceSampleRate = 44100;
    private ResampleSampleProvider? _resampleProvider;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    public int SourceSampleRate
    {
        get => _sourceSampleRate;
        set
        {
            if (_sourceSampleRate != value)
            {
                _sourceSampleRate = value;
                _resampleProvider?.Dispose();
                _resampleProvider = null;
                _lastTimeRangeEnd = null;
            }
        }
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Resample node requires exactly one input.");

        var newContext = new AudioProcessContext(context.TimeRange, SourceSampleRate, context.AnimationSampler, context.OriginalTimeRange);
        return Resample(context, Inputs[0].Process(newContext));
    }

    public override AudioBuffer Flush(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Resample node requires exactly one input.");

        var newContext = new AudioProcessContext(context.TimeRange, SourceSampleRate, context.AnimationSampler, context.OriginalTimeRange);
        return Resample(context, Inputs[0].Flush(newContext));
    }

    public override int GetTotalLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int sourceLatency = base.GetTotalLatencySamples(SourceSampleRate);
        if (sourceLatency == 0 || sourceLatency == int.MaxValue)
            return sourceLatency;

        double scaled = Math.Ceiling(sourceLatency * (double)sampleRate / SourceSampleRate);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    private AudioBuffer Resample(AudioProcessContext context, AudioBuffer input)
    {
        // Same rate: pass the input through (caller owns it, don't dispose).
        if (input.SampleRate == context.SampleRate)
        {
            _resampleProvider?.Dispose();
            _resampleProvider = null;
            _lastTimeRangeEnd = context.TimeRange.End;
            return input;
        }

        if (_lastTimeRangeEnd is { } previousEnd && !context.ContinuesFrom(previousEnd))
        {
            _resampleProvider?.Dispose();
            _resampleProvider = null;
        }

        if (_resampleProvider == null
            || _lastSampleRate != context.SampleRate
            || _resampleProvider.SourceSampleRate != input.SampleRate
            || _resampleProvider.ChannelCount != input.ChannelCount)
        {
            _resampleProvider?.Dispose();
            _resampleProvider = new ResampleSampleProvider(input, context.SampleRate);
            _lastSampleRate = context.SampleRate;
        }
        else
        {
            _resampleProvider.Append(input);
        }

        try
        {
            AudioBuffer output = _resampleProvider.Read(context.GetSampleCount());
            _lastTimeRangeEnd = context.TimeRange.End;
            return output;
        }
        catch
        {
            _resampleProvider?.Dispose();
            _resampleProvider = null;
            _lastTimeRangeEnd = null;
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resampleProvider?.Dispose();
            _resampleProvider = null;
            _lastTimeRangeEnd = null;
        }

        base.Dispose(disposing);
    }

    private sealed class ResampleSampleProvider : IDisposable
    {
        private readonly int _targetSampleRate;
        private readonly int _sourceSampleRate;
        private readonly int _channelCount;
        private WdlResamplingSampleProvider? _wdlResampler;
        private readonly AudioBufferSampleProvider _inputProvider;
        private bool _disposed;

        public ResampleSampleProvider(AudioBuffer input, int targetSampleRate)
        {
            _targetSampleRate = targetSampleRate;
            _sourceSampleRate = input.SampleRate;
            _channelCount = input.ChannelCount;
            _inputProvider = new AudioBufferSampleProvider(input);
            _wdlResampler = new WdlResamplingSampleProvider(_inputProvider, _targetSampleRate);
        }

        public int SourceSampleRate => _sourceSampleRate;

        public int ChannelCount => _channelCount;

        public void Append(AudioBuffer input)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResampleSampleProvider));
            if (input.SampleRate != _sourceSampleRate || input.ChannelCount != _channelCount)
                throw new InvalidOperationException("Resample input format changed while streaming.");

            _inputProvider.Append(input);
        }

        public AudioBuffer Read(int sampleCount)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResampleSampleProvider));

            var output = new AudioBuffer(_targetSampleRate, _channelCount, sampleCount);
            try
            {
                var buffer = new float[sampleCount * _channelCount];
                int samplesRead = _wdlResampler!.Read(buffer, 0, buffer.Length);

                // Copy interleaved samples back to AudioBuffer
                for (int ch = 0; ch < _channelCount; ch++)
                {
                    var channelData = output.GetChannelData(ch);
                    for (int i = 0; i < samplesRead / _channelCount; i++)
                    {
                        channelData[i] = buffer[i * _channelCount + ch];
                    }
                }

                return output;
            }
            catch
            {
                // Dispose the output the caller never received rather than leak it.
                output.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _wdlResampler = null;
                _inputProvider.Dispose();
                _disposed = true;
            }
        }
    }

    private sealed class AudioBufferSampleProvider : ISampleProvider, IDisposable
    {
        private readonly WaveFormat _waveFormat;
        private readonly Queue<AudioBuffer> _inputBuffers = new();
        private AudioBuffer? _currentBuffer;
        private int _position;
        private bool _disposed;

        public AudioBufferSampleProvider(AudioBuffer buffer)
        {
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(buffer.SampleRate, buffer.ChannelCount);
            _inputBuffers.Enqueue(buffer);
        }

        public WaveFormat WaveFormat => _waveFormat;

        private int ChannelCount => _waveFormat.Channels;

        public void Append(AudioBuffer buffer)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioBufferSampleProvider));
            if (buffer.SampleRate != _waveFormat.SampleRate || buffer.ChannelCount != ChannelCount)
                throw new InvalidOperationException("Resample input format changed while streaming.");

            _inputBuffers.Enqueue(buffer);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_disposed)
                return 0;

            int total = 0;
            while (total < count)
            {
                if (_currentBuffer is null)
                {
                    if (_inputBuffers.Count == 0)
                        break;

                    _currentBuffer = _inputBuffers.Dequeue();
                    _position = 0;
                }

                int samplesPerChannel = (count - total) / ChannelCount;
                int availableSamples = _currentBuffer.SampleCount - _position;
                int samplesToRead = Math.Min(samplesPerChannel, availableSamples);
                if (samplesToRead <= 0)
                {
                    _currentBuffer.Dispose();
                    _currentBuffer = null;
                    continue;
                }

                for (int i = 0; i < samplesToRead; i++)
                {
                    for (int ch = 0; ch < ChannelCount; ch++)
                    {
                        buffer[offset + total + i * ChannelCount + ch]
                            = _currentBuffer.GetChannelData(ch)[_position + i];
                    }
                }

                total += samplesToRead * ChannelCount;
                _position += samplesToRead;
                if (_position == _currentBuffer.SampleCount)
                {
                    _currentBuffer.Dispose();
                    _currentBuffer = null;
                }
            }

            return total;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _currentBuffer?.Dispose();
                _currentBuffer = null;
                while (_inputBuffers.Count > 0)
                {
                    _inputBuffers.Dequeue().Dispose();
                }

                _disposed = true;
            }
        }
    }
}
