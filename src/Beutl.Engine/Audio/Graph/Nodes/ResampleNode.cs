using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Beutl.Audio.Graph.Nodes;

public sealed class ResampleNode : AudioNode
{
    private int _sourceSampleRate = 44100;
    private ResampleSampleProvider? _resampleProvider;
    private int _lastSampleRate;

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
            }
        }
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Resample node requires exactly one input.");

        var newContext = new AudioProcessContext(context.TimeRange, SourceSampleRate, context.AnimationSampler, context.OriginalTimeRange);
        var input = Inputs[0].Process(newContext);

        if (input.SampleRate == context.SampleRate)
            return input;

        if (_resampleProvider == null || _lastSampleRate != context.SampleRate)
        {
            _resampleProvider?.Dispose();
            _resampleProvider = new ResampleSampleProvider(input, context.SampleRate);
            _lastSampleRate = context.SampleRate;
        }

        // Convert input to the resampler and process
        var output = _resampleProvider.Process(input);

        return output;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resampleProvider?.Dispose();
            _resampleProvider = null;
        }

        base.Dispose(disposing);
    }

    private sealed class ResampleSampleProvider : IDisposable
    {
        private readonly int _targetSampleRate;
        private WdlResamplingSampleProvider? _wdlResampler;
        private AudioBufferSampleProvider? _inputProvider;
        private bool _disposed;

        public ResampleSampleProvider(AudioBuffer input, int targetSampleRate)
        {
            _targetSampleRate = targetSampleRate;
            CreateResampler(input);
        }

        private void CreateResampler(AudioBuffer input)
        {
            _inputProvider?.Dispose();
            _wdlResampler = null;

            _inputProvider = new AudioBufferSampleProvider(input);
            _wdlResampler = new WdlResamplingSampleProvider(_inputProvider, _targetSampleRate);
        }

        public AudioBuffer Process(AudioBuffer input)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResampleSampleProvider));

            // Update input if it has changed
            if (_inputProvider == null || _inputProvider.InputBuffer != input)
            {
                CreateResampler(input);
            }

            var outputSampleCount = (int)System.Math.Round(input.SampleCount * (double)_targetSampleRate / input.SampleRate);
            var output = new AudioBuffer(_targetSampleRate, input.ChannelCount, outputSampleCount);

            // Read resampled data
            var buffer = new float[outputSampleCount * input.ChannelCount];
            int samplesRead = _wdlResampler!.Read(buffer, 0, buffer.Length);

            // Copy interleaved samples back to AudioBuffer
            for (int ch = 0; ch < input.ChannelCount; ch++)
            {
                var channelData = output.GetChannelData(ch);
                for (int i = 0; i < samplesRead / input.ChannelCount; i++)
                {
                    channelData[i] = buffer[i * input.ChannelCount + ch];
                }
            }

            return output;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _wdlResampler = null;
                _inputProvider?.Dispose();
                _disposed = true;
            }
        }
    }

    private sealed class AudioBufferSampleProvider : ISampleProvider, IDisposable
    {
        private readonly WaveFormat _waveFormat;
        private AudioBuffer? _inputBuffer;
        private int _position;
        private bool _disposed;

        public AudioBufferSampleProvider(AudioBuffer buffer)
        {
            _inputBuffer = buffer;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(buffer.SampleRate, buffer.ChannelCount);
            _position = 0;
        }

        public AudioBuffer? InputBuffer => _inputBuffer;

        public WaveFormat WaveFormat => _waveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (_disposed || _inputBuffer == null)
                return 0;

            var samplesPerChannel = count / _inputBuffer.ChannelCount;
            var availableSamples = _inputBuffer.SampleCount - _position;
            var samplesToRead = System.Math.Min(samplesPerChannel, availableSamples);

            if (samplesToRead <= 0)
                return 0;

            // Copy samples from AudioBuffer to interleaved float array
            for (int i = 0; i < samplesToRead; i++)
            {
                for (int ch = 0; ch < _inputBuffer.ChannelCount; ch++)
                {
                    var channelData = _inputBuffer.GetChannelData(ch);
                    buffer[offset + i * _inputBuffer.ChannelCount + ch] = channelData[_position + i];
                }
            }

            _position += samplesToRead;
            return samplesToRead * _inputBuffer.ChannelCount;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _inputBuffer = null;
                _disposed = true;
            }
        }
    }
}
