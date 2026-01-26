using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Audio node that performs time stretching while preserving pitch using WSOLA algorithm.
/// Unlike SpeedNode which uses resampling (changes pitch), this node maintains the original pitch.
/// </summary>
public sealed class TimeStretchNode : AudioNode
{
    private TimeStretchProcessor? _processor;
    private int _lastSampleRate;

    /// <summary>
    /// Speed percentage (25-400). 100 = normal speed.
    /// </summary>
    public required IProperty<float> Speed { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("TimeStretch node requires exactly one input.");

        // Calculate the expected output sample count based on the context's time range
        var expectedOutputSampleCount = context.GetSampleCount();

        // Initialize processor if needed
        if (_processor == null || _lastSampleRate != context.SampleRate)
        {
            _processor?.Dispose();
            _processor = new TimeStretchProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
        }

        return ProcessStaticSpeed(context, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessStaticSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        float speed = (Speed?.CurrentValue ?? 100f) / 100f;

        // If speed is 1.0, use normal processing
        if (Math.Abs(speed - 1.0f) < float.Epsilon)
        {
            return Inputs[0].Process(context);
        }

        // Calculate source time range from output time range
        // For speed > 1: we need more input to produce the same output
        // For speed < 1: we need less input to produce the same output
        var sourceTimeRange = CalculateSourceTimeRange(context.TimeRange, speed);

        // Create input context with calculated source time range
        var inputContext = new AudioProcessContext(
            sourceTimeRange,
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        // Process with WSOLA to get the expected output length
        return _processor!.ProcessBuffer(inputContext, speed, expectedOutputSampleCount);
    }

    private TimeRange CalculateSourceTimeRange(TimeRange outputTimeRange, float speed)
    {
        // Source start time scales with speed
        var sourceStart = outputTimeRange.Start * speed;
        // Source duration scales with speed (need more input for faster playback)
        var sourceDuration = outputTimeRange.Duration * speed;

        return new TimeRange(sourceStart, sourceDuration);
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
        private readonly int _channels;
        private readonly TimeStretchNode _node;
        private readonly WsolaProcessor[] _channelProcessors;
        private readonly WsolaConfig _config;

        public TimeStretchProcessor(int sampleRate, int channels, TimeStretchNode node)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _node = node;
            _config = WsolaConfig.Default;

            _channelProcessors = new WsolaProcessor[channels];
            for (int i = 0; i < channels; i++)
            {
                _channelProcessors[i] = new WsolaProcessor(sampleRate, _config);
            }
        }

        public AudioBuffer ProcessBuffer(AudioProcessContext context, float speed, int expectedOutputSamples)
        {
            // Clamp speed to valid range
            speed = Math.Clamp(speed, 0.25f, 4.0f);

            // Get input from the node's input
            var input = _node.Inputs[0].Process(context);

            var output = new AudioBuffer(_sampleRate, _channels, expectedOutputSamples);

            for (int ch = 0; ch < Math.Min(input.ChannelCount, _channels); ch++)
            {
                var inData = input.GetChannelData(ch);
                var outData = output.GetChannelData(ch);

                // Process with WSOLA, retry until we have enough output
                int totalWritten = 0;
                bool firstPass = true;

                while (totalWritten < expectedOutputSamples)
                {
                    // On first pass, provide input data; on subsequent passes, use empty input
                    // (WSOLA processor has internal buffer that may still produce output)
                    var inputSpan = firstPass ? (ReadOnlySpan<float>)inData : ReadOnlySpan<float>.Empty;
                    var outputSpan = outData.Slice(totalWritten);

                    int written = _channelProcessors[ch].Process(inputSpan, speed, outputSpan);

                    if (written == 0)
                        break; // No more output possible

                    totalWritten += written;
                    firstPass = false;
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
