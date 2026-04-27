using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class CompressorNode : AudioNode
{
    private const float MinDb = -100f;

    private float _envelopeDb = MinDb;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Ratio { get; init; }

    public required IProperty<float> Attack { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Knee { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Compressor node requires exactly one input.");

        var input = Inputs[0].Process(context);

        if (_lastSampleRate != context.SampleRate)
        {
            Reset();
            _lastSampleRate = context.SampleRate;
        }

        // Reset whenever the chunk does not continue directly from the previous one, because the
        // node instance is cached across Compose() calls and stale envelope state would otherwise
        // bleed into the first samples after a seek or stop/restart.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            Reset();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        bool hasAnimation = Threshold.Animation != null ||
                            Ratio.Animation != null ||
                            Attack.Animation != null ||
                            Release.Animation != null ||
                            Knee.Animation != null ||
                            MakeupGain.Animation != null;

        if (!hasAnimation)
        {
            return ProcessStatic(input, context);
        }

        return ProcessAnimated(input, context);
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        float threshold = Threshold.CurrentValue;
        float ratio = MathF.Max(1f, Ratio.CurrentValue);
        float attackMs = MathF.Max(0.01f, Attack.CurrentValue);
        float releaseMs = MathF.Max(0.01f, Release.CurrentValue);
        float knee = MathF.Max(0f, Knee.CurrentValue);
        float makeupGain = MakeupGain.CurrentValue;

        float attackCoeff = ComputeCoeff(attackMs, context.SampleRate);
        float releaseCoeff = ComputeCoeff(releaseMs, context.SampleRate);
        float slope = 1f - 1f / ratio;
        float makeupLinear = AudioMath.ConvertDbToLinear(makeupGain);

        int channels = input.ChannelCount;
        for (int i = 0; i < input.SampleCount; i++)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                float a = MathF.Abs(input.GetChannelData(ch)[i]);
                if (a > peak) peak = a;
            }

            float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;
            float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
            _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);

            float gainReductionDb = ComputeGainReductionDb(_envelopeDb, threshold, knee, slope);
            float gainLinear = AudioMath.ConvertDbToLinear(-gainReductionDb) * makeupLinear;

            for (int ch = 0; ch < channels; ch++)
            {
                output.GetChannelData(ch)[i] = input.GetChannelData(ch)[i] * gainLinear;
            }
        }

        return output;
    }

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        const int maxChunkSize = 1024;
        int bufferSize = Math.Min(maxChunkSize, input.SampleCount);
        Span<float> thresholds = stackalloc float[bufferSize];
        Span<float> ratios = stackalloc float[bufferSize];
        Span<float> attacks = stackalloc float[bufferSize];
        Span<float> releases = stackalloc float[bufferSize];
        Span<float> knees = stackalloc float[bufferSize];
        Span<float> makeups = stackalloc float[bufferSize];

        int channels = input.ChannelCount;
        int processed = 0;
        while (processed < input.SampleCount)
        {
            int chunkSize = Math.Min(bufferSize, input.SampleCount - processed);

            var chunkStart = context.GetTimeForSample(processed);
            var chunkEnd = context.GetTimeForSample(processed + chunkSize);
            var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

            context.AnimationSampler.SampleBuffer(Threshold, chunkRange, context.SampleRate, thresholds[..chunkSize]);
            context.AnimationSampler.SampleBuffer(Ratio, chunkRange, context.SampleRate, ratios[..chunkSize]);
            context.AnimationSampler.SampleBuffer(Attack, chunkRange, context.SampleRate, attacks[..chunkSize]);
            context.AnimationSampler.SampleBuffer(Release, chunkRange, context.SampleRate, releases[..chunkSize]);
            context.AnimationSampler.SampleBuffer(Knee, chunkRange, context.SampleRate, knees[..chunkSize]);
            context.AnimationSampler.SampleBuffer(MakeupGain, chunkRange, context.SampleRate, makeups[..chunkSize]);

            for (int i = 0; i < chunkSize; i++)
            {
                int idx = processed + i;
                float peak = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    float a = MathF.Abs(input.GetChannelData(ch)[idx]);
                    if (a > peak) peak = a;
                }

                float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;

                float threshold = thresholds[i];
                float ratio = MathF.Max(1f, ratios[i]);
                float attackMs = MathF.Max(0.01f, attacks[i]);
                float releaseMs = MathF.Max(0.01f, releases[i]);
                float knee = MathF.Max(0f, knees[i]);
                float makeupDb = makeups[i];

                float attackCoeff = ComputeCoeff(attackMs, context.SampleRate);
                float releaseCoeff = ComputeCoeff(releaseMs, context.SampleRate);
                float slope = 1f - 1f / ratio;

                float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
                _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);

                float gainReductionDb = ComputeGainReductionDb(_envelopeDb, threshold, knee, slope);
                float gainLinear = AudioMath.ConvertDbToLinear(makeupDb - gainReductionDb);

                for (int ch = 0; ch < channels; ch++)
                {
                    output.GetChannelData(ch)[idx] = input.GetChannelData(ch)[idx] * gainLinear;
                }
            }

            processed += chunkSize;
        }

        return output;
    }

    private static float ComputeCoeff(float timeMs, int sampleRate)
    {
        return MathF.Exp(-1f / (timeMs * 0.001f * sampleRate));
    }

    private static float ComputeGainReductionDb(float envelopeDb, float thresholdDb, float kneeDb, float slope)
    {
        if (kneeDb > 0f)
        {
            float halfKnee = kneeDb * 0.5f;
            float diff = envelopeDb - thresholdDb;
            if (diff <= -halfKnee)
            {
                return 0f;
            }
            if (diff < halfKnee)
            {
                float x = diff + halfKnee;
                return slope * x * x / (2f * kneeDb);
            }
            return slope * diff;
        }

        return envelopeDb > thresholdDb ? slope * (envelopeDb - thresholdDb) : 0f;
    }

    public void Reset()
    {
        _envelopeDb = MinDb;
    }
}
