using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class RingModulatorNode : AudioNode
{
    private const double Tau = Math.PI * 2;

    private double _phase;
    private MediaTime? _lastTimeRangeEnd;

    public required IProperty<float> Frequency { get; init; }

    public required IProperty<float> Mix { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Ring modulator node requires exactly one input.");

        return RecordProcessedOutput(ProcessTail(Inputs[0].Process(context), context, draining: false));
    }

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining)
    {
        using AudioBuffer owned = input;
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            bool continues = _lastTimeRangeEnd is { } end && context.ContinuesFrom(end);
            _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

            Span<float> frequencies = stackalloc float[Math.Min(input.SampleCount, 1024)];
            Span<float> mixes = stackalloc float[frequencies.Length];
            Span<float> carrier = stackalloc float[frequencies.Length];
            double step = Tau / context.SampleRate;

            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = Math.Min(frequencies.Length, input.SampleCount - processed);
                TimeRange chunkRange = context.GetChunkRange(processed, chunkSize);
                context.AnimationSampler.SampleBuffer(Frequency, chunkRange, context.SampleRate, frequencies[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Mix, chunkRange, context.SampleRate, mixes[..chunkSize]);

                // From the time alone, so that a frequency that stays the same gives the same result after a seek.
                if (!continues)
                {
                    _phase = Tau * frequencies[0] * context.TimeRange.Start.ToSeconds() % Tau;
                    continues = true;
                }

                for (int i = 0; i < chunkSize; i++)
                {
                    float mix = mixes[i] / 100f;
                    carrier[i] = 1f - mix + mix * (float)Math.Sin(_phase);
                    _phase = (_phase + step * frequencies[i]) % Tau;
                }

                for (int ch = 0; ch < input.ChannelCount; ch++)
                {
                    ReadOnlySpan<float> inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    Span<float> outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    for (int i = 0; i < chunkSize; i++)
                    {
                        outData[i] = inData[i] * carrier[i];
                    }
                }

                processed += chunkSize;
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
