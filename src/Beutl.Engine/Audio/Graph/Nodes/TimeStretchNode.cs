using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Audio node that performs time stretching while preserving pitch using WSOLA algorithm.
/// </summary>
public sealed class TimeStretchNode : AudioNode
{
    private TimeStretchProcessor? _processor;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeStart;

    /// <summary>
    /// Speed percentage (25-400). 100 = normal speed.
    /// </summary>
    public required IProperty<float> Speed { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("TimeStretch node requires exactly one input.");

        var input = Inputs[0].Process(context);

        // Speed 100% = no change, pass through
        float speed = (Speed?.CurrentValue ?? 100f) / 100f;
        if (Math.Abs(speed - 1.0f) < float.Epsilon)
        {
            return input;
        }

        // Initialize processor if needed or sample rate changed
        if (_processor == null || _lastSampleRate != context.SampleRate)
        {
            _processor?.Dispose();
            _processor = new TimeStretchProcessor(context.SampleRate, input.ChannelCount);
            _lastSampleRate = context.SampleRate;
        }

        // Reset on seek
        if (!_lastTimeRangeStart.HasValue || _lastTimeRangeStart.Value > context.TimeRange.Start)
        {
            _processor.Reset();
            _lastTimeRangeStart = context.TimeRange.Start;
        }

        // Check for animation
        bool hasAnimation = Speed?.IsAnimatable == true;

        if (!hasAnimation)
        {
            return ProcessStaticSpeed(input, context, speed);
        }

        return ProcessAnimatedSpeed(input, context);
    }

    private AudioBuffer ProcessStaticSpeed(AudioBuffer input, AudioProcessContext context, float speed)
    {
        return _processor!.Process(input, speed);
    }

    private AudioBuffer ProcessAnimatedSpeed(AudioBuffer input, AudioProcessContext context)
    {
        // For animated speed, sample the speed values
        int sampleCount = input.SampleCount;
        Span<float> speeds = stackalloc float[Math.Min(sampleCount, 1024)];

        context.AnimationSampler.SampleBuffer(Speed, context.TimeRange, context.SampleRate, speeds[..Math.Min(sampleCount, speeds.Length)]);

        // Use average speed for this block
        float avgSpeed = 0f;
        int count = Math.Min(sampleCount, speeds.Length);
        for (int i = 0; i < count; i++)
        {
            avgSpeed += speeds[i];
        }
        avgSpeed = (avgSpeed / count) / 100f;

        return _processor!.Process(input, avgSpeed);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _processor?.Dispose();
            _processor = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Internal processor that manages WSOLA processing for multiple channels.
    /// </summary>
    private sealed class TimeStretchProcessor : IDisposable
    {
        private readonly int _sampleRate;
        private readonly int _channelCount;
        private readonly WsolaProcessor[] _channelProcessors;
        private readonly WsolaConfig _config;

        public TimeStretchProcessor(int sampleRate, int channelCount)
        {
            _sampleRate = sampleRate;
            _channelCount = channelCount;
            _config = WsolaConfig.Default;

            _channelProcessors = new WsolaProcessor[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                _channelProcessors[i] = new WsolaProcessor(sampleRate, _config);
            }
        }

        public AudioBuffer Process(AudioBuffer input, float speed)
        {
            // Clamp speed to valid range
            speed = Math.Clamp(speed, 0.25f, 4.0f);

            // Calculate expected output length based on speed
            int inputSamples = input.SampleCount;
            int expectedOutputSamples = (int)(inputSamples / speed);

            var output = new AudioBuffer(_sampleRate, _channelCount, expectedOutputSamples);

            for (int ch = 0; ch < Math.Min(input.ChannelCount, _channelCount); ch++)
            {
                var inData = input.GetChannelData(ch);
                var outData = output.GetChannelData(ch);

                // Convert to span for WSOLA processing
                ReadOnlySpan<float> inputSpan = inData;

                int written = _channelProcessors[ch].Process(inputSpan, speed, outData);

                // Fill remaining with last sample if needed
                if (written < expectedOutputSamples && written > 0)
                {
                    float lastSample = outData[written - 1];
                    for (int i = written; i < expectedOutputSamples; i++)
                    {
                        outData[i] = lastSample;
                    }
                }
            }

            return output;
        }

        public void Reset()
        {
            foreach (var processor in _channelProcessors)
            {
                processor.Reset();
            }
        }

        public void Dispose()
        {
            foreach (var processor in _channelProcessors)
            {
                processor.Dispose();
            }
        }
    }
}
