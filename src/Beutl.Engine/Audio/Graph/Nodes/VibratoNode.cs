using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class VibratoNode : AudioNode
{
    private const double Tau = Math.PI * 2;

    private DelayLines? _delayLines;
    private int _lastSampleRate;
    private double _phase;
    private float _lastRate;
    private float _lastDepth;
    private MediaTime? _lastTimeRangeEnd;

    public required IProperty<float> Rate { get; init; }

    public required IProperty<float> Depth { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Vibrato node requires exactly one input.");

        return RecordProcessedOutput(ProcessTail(Inputs[0].Process(context), context, draining: false));
    }

    public override int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return VibratoEffect.GetHeldSamples(Depth, sampleRate);
    }

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining)
    {
        using AudioBuffer owned = input;
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            if (_delayLines == null || _lastSampleRate != context.SampleRate || _delayLines.ChannelCount != input.ChannelCount)
            {
                _delayLines?.Dispose();
                // The delay reaches twice the depth.
                _delayLines = new DelayLines(
                    input.ChannelCount, (int)Math.Ceiling(VibratoEffect.DepthMax * 2 / 1000 * context.SampleRate));
                _lastSampleRate = context.SampleRate;
                _lastTimeRangeEnd = null;
            }

            bool continues = _lastTimeRangeEnd is { } end && context.ContinuesFrom(end);
            _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;
            if (!continues)
                _delayLines.Clear();

            Span<float> rates = stackalloc float[Math.Min(input.SampleCount, 1024)];
            Span<float> depths = stackalloc float[rates.Length];
            Span<float> delays = stackalloc float[rates.Length];
            double step = Tau / context.SampleRate;
            double samplesPerMillisecond = context.SampleRate / 1000d;
            double maxDelay = VibratoEffect.DepthMax * 2 * samplesPerMillisecond;

            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = Math.Min(rates.Length, input.SampleCount - processed);
                TimeRange chunkRange = context.GetChunkRange(processed, chunkSize);
                // The tail comes after the clip, where the keyframes are for a part that was trimmed away.
                if (draining)
                {
                    rates[..chunkSize].Fill(_lastRate);
                    depths[..chunkSize].Fill(_lastDepth);
                }
                else
                {
                    context.AnimationSampler.SampleBuffer(Rate, chunkRange, context.SampleRate, rates[..chunkSize]);
                    context.AnimationSampler.SampleBuffer(Depth, chunkRange, context.SampleRate, depths[..chunkSize]);
                    _lastRate = rates[chunkSize - 1];
                    _lastDepth = depths[chunkSize - 1];
                }

                // From the time alone, so that a rate that stays the same gives the same result after a seek.
                if (!continues)
                {
                    _phase = Tau * rates[0] * context.TimeRange.Start.ToSeconds() % Tau;
                    continues = true;
                }

                for (int i = 0; i < chunkSize; i++)
                {
                    // No delay at the start of the swing, so a clip starts on time. Easing can carry an animated
                    // depth past its range, while the delay line only reaches over the range.
                    double delay = depths[i] * samplesPerMillisecond * (1 - Math.Cos(_phase));
                    delays[i] = (float)(delay > 0 ? Math.Min(delay, maxDelay) : 0);
                    _phase = (_phase + step * rates[i]) % Tau;
                }

                for (int ch = 0; ch < input.ChannelCount; ch++)
                {
                    ReadOnlySpan<float> inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    Span<float> outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    for (int i = 0; i < chunkSize; i++)
                    {
                        _delayLines.Write(ch, inData[i]);
                        outData[i] = _delayLines.Read(ch, delays[i]);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _delayLines?.Dispose();

        base.Dispose(disposing);
    }
}
