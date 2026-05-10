using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Audio.Graph.Nodes;

public sealed class CompressorNode : AudioNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<CompressorNode>();

    private const float MinDb = -100f;

    // Lower bounds match the [Range] attributes declared on CompressorEffect. Keeping them in sync
    // avoids silent clamping when animation values overshoot the declared range.
    private const float MinAttackMs = 0.1f;
    private const float MinReleaseMs = 1f;
    private const float MinRatio = 1f;
    private const float MinKneeDb = 0f;

    private float _envelopeDb = MinDb;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    // Latched per node instance so the warning only fires once per non-finite event class, even
    // when the corruption persists across thousands of samples.
    private bool _loggedNonFiniteEnvelope;
    private bool _loggedNonFiniteSample;
    private bool _loggedNonFiniteParameter;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Ratio { get; init; }

    public required IProperty<float> Attack { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Knee { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException(
                $"Compressor node requires exactly one input but got {Inputs.Count}.");

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

        float threshold = SafeParameter(Threshold.CurrentValue, Threshold.DefaultValue, nameof(Threshold));
        float ratio = MathF.Max(MinRatio, SafeParameter(Ratio.CurrentValue, Ratio.DefaultValue, nameof(Ratio)));
        float attackMs = MathF.Max(MinAttackMs, SafeParameter(Attack.CurrentValue, Attack.DefaultValue, nameof(Attack)));
        float releaseMs = MathF.Max(MinReleaseMs, SafeParameter(Release.CurrentValue, Release.DefaultValue, nameof(Release)));
        float knee = MathF.Max(MinKneeDb, SafeParameter(Knee.CurrentValue, Knee.DefaultValue, nameof(Knee)));
        float makeupGain = SafeParameter(MakeupGain.CurrentValue, MakeupGain.DefaultValue, nameof(MakeupGain));

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
            RecoverEnvelopeIfNonFinite();

            float gainReductionDb = ComputeGainReductionDb(_envelopeDb, threshold, knee, slope);
            float gainLinear = AudioMath.ConvertDbToLinear(-gainReductionDb) * makeupLinear;

            for (int ch = 0; ch < channels; ch++)
            {
                float sample = input.GetChannelData(ch)[i] * gainLinear;
                output.GetChannelData(ch)[i] = SanitizeOutput(sample);
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

        // Final fallbacks used when an animated parameter samples to NaN/Infinity (e.g. malformed
        // KeyFrame). Without this guard, a single non-finite animated value would propagate into
        // the gain calculation and cause the output sanitizer to silently zero out every sample.
        float thresholdFallback = SafeParameter(Threshold.CurrentValue, Threshold.DefaultValue, nameof(Threshold));
        float ratioFallback = MathF.Max(MinRatio, SafeParameter(Ratio.CurrentValue, Ratio.DefaultValue, nameof(Ratio)));
        float attackFallback = MathF.Max(MinAttackMs, SafeParameter(Attack.CurrentValue, Attack.DefaultValue, nameof(Attack)));
        float releaseFallback = MathF.Max(MinReleaseMs, SafeParameter(Release.CurrentValue, Release.DefaultValue, nameof(Release)));
        float kneeFallback = MathF.Max(MinKneeDb, SafeParameter(Knee.CurrentValue, Knee.DefaultValue, nameof(Knee)));
        float makeupFallback = SafeParameter(MakeupGain.CurrentValue, MakeupGain.DefaultValue, nameof(MakeupGain));

        int channels = input.ChannelCount;
        int processed = 0;

        // lastAttackMs/lastReleaseMs are seeded with NaN so the first comparison is always
        // unequal and the coefficients get computed on the very first sample. After that, Exp
        // is only called when the animated ms value actually changes.
        float lastAttackMs = float.NaN;
        float lastReleaseMs = float.NaN;
        float attackCoeff = 0f;
        float releaseCoeff = 0f;

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

                float threshold = SafeParameter(thresholds[i], thresholdFallback, nameof(Threshold));
                float ratio = MathF.Max(MinRatio, SafeParameter(ratios[i], ratioFallback, nameof(Ratio)));
                float attackMs = MathF.Max(MinAttackMs, SafeParameter(attacks[i], attackFallback, nameof(Attack)));
                float releaseMs = MathF.Max(MinReleaseMs, SafeParameter(releases[i], releaseFallback, nameof(Release)));
                float knee = MathF.Max(MinKneeDb, SafeParameter(knees[i], kneeFallback, nameof(Knee)));
                float makeupDb = SafeParameter(makeups[i], makeupFallback, nameof(MakeupGain));

                if (attackMs != lastAttackMs)
                {
                    attackCoeff = ComputeCoeff(attackMs, context.SampleRate);
                    lastAttackMs = attackMs;
                }
                if (releaseMs != lastReleaseMs)
                {
                    releaseCoeff = ComputeCoeff(releaseMs, context.SampleRate);
                    lastReleaseMs = releaseMs;
                }
                float slope = 1f - 1f / ratio;

                float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
                _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);
                RecoverEnvelopeIfNonFinite();

                float gainReductionDb = ComputeGainReductionDb(_envelopeDb, threshold, knee, slope);
                float gainLinear = AudioMath.ConvertDbToLinear(makeupDb - gainReductionDb);

                for (int ch = 0; ch < channels; ch++)
                {
                    float sample = input.GetChannelData(ch)[idx] * gainLinear;
                    output.GetChannelData(ch)[idx] = SanitizeOutput(sample);
                }
            }

            processed += chunkSize;
        }

        return output;
    }

    // Without this guard, a single non-finite envelope sample would permanently poison the state
    // until the next Reset(). The first occurrence is logged; subsequent ones are suppressed so
    // the audio thread is not spammed.
    private void RecoverEnvelopeIfNonFinite()
    {
        if (float.IsFinite(_envelopeDb)) return;
        _envelopeDb = MinDb;
        if (_loggedNonFiniteEnvelope) return;
        s_logger.LogWarning(
            "Compressor envelope became non-finite (input sample produced inf/NaN); resetting to {MinDb} dB. Further occurrences will be suppressed.",
            MinDb);
        _loggedNonFiniteEnvelope = true;
    }

    private float SafeParameter(float value, float fallback, string paramName)
    {
        if (float.IsFinite(value)) return value;
        if (!_loggedNonFiniteParameter)
        {
            s_logger.LogWarning(
                "Compressor parameter '{Param}' produced a non-finite value; falling back to {Fallback}. Further occurrences will be suppressed.",
                paramName, fallback);
            _loggedNonFiniteParameter = true;
        }
        return fallback;
    }

    private float SanitizeOutput(float sample)
    {
        if (float.IsFinite(sample)) return sample;
        if (!_loggedNonFiniteSample)
        {
            s_logger.LogWarning(
                "Compressor produced a non-finite output sample; replacing with 0 to protect downstream nodes. Further occurrences will be suppressed.");
            _loggedNonFiniteSample = true;
        }
        return 0f;
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
