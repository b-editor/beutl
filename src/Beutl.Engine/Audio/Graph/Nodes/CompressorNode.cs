using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class CompressorNode : AudioNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<CompressorNode>();

    private const float MinDb = -100f;

    private float _envelopeDb = MinDb;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    // Latched per node instance so the warning only fires once per non-finite event class, even
    // when the corruption persists across thousands of samples.
    private bool _loggedNonFiniteEnvelope;
    private bool _loggedNonFiniteSample;
    // Per-parameter so a non-finite sample on one parameter (e.g. Attack) does not silently
    // suppress diagnostics for an unrelated parameter (e.g. Threshold) later in the stream.
    private readonly HashSet<string> _loggedNonFiniteParameters = new();

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

        if (input.SampleCount == 0)
        {
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

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

        EffectiveParameters p = ReadStaticParameters();

        float attackCoeff = ComputeCoeff(p.Attack, context.SampleRate);
        float releaseCoeff = ComputeCoeff(p.Release, context.SampleRate);
        float slope = 1f - 1f / p.Ratio;
        float makeupLinear = AudioMath.ConvertDbToLinear(p.MakeupGain);

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

            float gainReductionDb = ComputeGainReductionDb(_envelopeDb, p.Threshold, p.Knee, slope);
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
        EffectiveParameters fallback = ReadStaticParameters();

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

                EffectiveParameters p = SanitizeAnimated(
                    thresholds[i], ratios[i], attacks[i], releases[i], knees[i], makeups[i], fallback);

                if (p.Attack != lastAttackMs)
                {
                    attackCoeff = ComputeCoeff(p.Attack, context.SampleRate);
                    lastAttackMs = p.Attack;
                }
                if (p.Release != lastReleaseMs)
                {
                    releaseCoeff = ComputeCoeff(p.Release, context.SampleRate);
                    lastReleaseMs = p.Release;
                }
                float slope = 1f - 1f / p.Ratio;

                float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
                _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);
                RecoverEnvelopeIfNonFinite();

                float gainReductionDb = ComputeGainReductionDb(_envelopeDb, p.Threshold, p.Knee, slope);
                float gainLinear = AudioMath.ConvertDbToLinear(p.MakeupGain - gainReductionDb);

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

    private EffectiveParameters ReadStaticParameters()
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(Threshold.CurrentValue, Threshold.DefaultValue, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Ratio = Sanitize(Ratio.CurrentValue, Ratio.DefaultValue, MinRatio, MaxRatio, nameof(Ratio)),
            Attack = Sanitize(Attack.CurrentValue, Attack.DefaultValue, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Release = Sanitize(Release.CurrentValue, Release.DefaultValue, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Knee = Sanitize(Knee.CurrentValue, Knee.DefaultValue, MinKneeDb, MaxKneeDb, nameof(Knee)),
            MakeupGain = Sanitize(MakeupGain.CurrentValue, MakeupGain.DefaultValue, MinMakeupGainDb, MaxMakeupGainDb, nameof(MakeupGain)),
        };
    }

    private EffectiveParameters SanitizeAnimated(
        float threshold, float ratio, float attack, float release, float knee, float makeup,
        in EffectiveParameters fallback)
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(threshold, fallback.Threshold, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Ratio = Sanitize(ratio, fallback.Ratio, MinRatio, MaxRatio, nameof(Ratio)),
            Attack = Sanitize(attack, fallback.Attack, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Release = Sanitize(release, fallback.Release, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Knee = Sanitize(knee, fallback.Knee, MinKneeDb, MaxKneeDb, nameof(Knee)),
            MakeupGain = Sanitize(makeup, fallback.MakeupGain, MinMakeupGainDb, MaxMakeupGainDb, nameof(MakeupGain)),
        };
    }

    // Substitute fallback for NaN/Infinity, then clamp to the parameter's declared [Range].
    // The clamp is what guards against an animated value silently bypassing the [Range] declaration
    // on CompressorEffect — without it, e.g. an animated Attack of 1e9 ms would freeze the
    // envelope without any diagnostic.
    private float Sanitize(float value, float fallback, float min, float max, string paramName)
    {
        return Math.Clamp(SafeParameter(value, fallback, paramName), min, max);
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
        if (_loggedNonFiniteParameters.Add(paramName))
        {
            s_logger.LogWarning(
                "Compressor parameter '{Param}' produced a non-finite value; falling back to {Fallback}. Further occurrences for this parameter will be suppressed.",
                paramName, fallback);
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

    // Internal so tests can drive an explicit reset, but not part of the public API: external
    // callers must not zero the envelope mid-buffer (it would produce an audible click).
    internal void Reset()
    {
        _envelopeDb = MinDb;
    }

    private struct EffectiveParameters
    {
        public float Threshold;
        public float Ratio;
        public float Attack;
        public float Release;
        public float Knee;
        public float MakeupGain;
    }
}
