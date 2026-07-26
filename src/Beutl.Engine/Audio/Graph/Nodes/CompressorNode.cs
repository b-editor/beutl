using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class CompressorNode : DynamicsNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<CompressorNode>();

    private float _envelopeDb = MinDb;

    private bool _loggedNonFiniteEnvelope;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Ratio { get; init; }

    public required IProperty<float> Attack { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Knee { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    protected override ILogger Logger => s_logger;

    protected override string DiagnosticName => "Compressor";

    // AnimationSampler does not evaluate expressions per sample, so expression-backed properties
    // remain at CurrentValue.
    protected override bool HasAnimatedParameters =>
        Threshold.Animation != null ||
        Ratio.Animation != null ||
        Attack.Animation != null ||
        Release.Animation != null ||
        Knee.Animation != null ||
        MakeupGain.Animation != null;

    protected override AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            EffectiveParameters p = ReadStaticParameters();

            float attackCoeff = ComputeCoeff(p.Attack, context.SampleRate);
            float releaseCoeff = ComputeCoeff(p.Release, context.SampleRate);
            float slope = 1f - 1f / p.Ratio;

            int channels = input.ChannelCount;
            int sampleCount = input.SampleCount;
            var (inputChannels, outputChannels) = MapChannels(input, output);

            // Materialize the channel spans ONCE for the mono/stereo fast path to avoid the per-sample
            // Memory.Span getter the >2-channel fallback still pays. Span<float>[] is impossible (ref
            // struct), hence the explicit locals.
            if (channels <= 2)
            {
                Span<float> in0 = inputChannels[0].Span;
                Span<float> out0 = outputChannels[0].Span;
                Span<float> in1 = channels == 2 ? inputChannels[1].Span : default;
                Span<float> out1 = channels == 2 ? outputChannels[1].Span : default;

                for (int i = 0; i < sampleCount; i++)
                {
                    float s0 = in0[i];
                    float peak = AccumulatePeak(0f, s0);
                    float s1 = 0f;
                    if (channels == 2)
                    {
                        s1 = in1[i];
                        peak = AccumulatePeak(peak, s1);
                    }

                    float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, slope);

                    out0[i] = SanitizeOutput(s0 * gainLinear);
                    if (channels == 2)
                    {
                        out1[i] = SanitizeOutput(s1 * gainLinear);
                    }
                }
            }
            else
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float peak = 0f;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        peak = AccumulatePeak(peak, inputChannels[ch].Span[i]);
                    }

                    float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, slope);

                    for (int ch = 0; ch < channels; ch++)
                    {
                        float sample = inputChannels[ch].Span[i] * gainLinear;
                        outputChannels[ch].Span[i] = SanitizeOutput(sample);
                    }
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

    protected override AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            const int maxChunkSize = 1024;
            int bufferSize = Math.Min(maxChunkSize, input.SampleCount);
            Span<float> thresholds = stackalloc float[bufferSize];
            Span<float> ratios = stackalloc float[bufferSize];
            Span<float> attacks = stackalloc float[bufferSize];
            Span<float> releases = stackalloc float[bufferSize];
            Span<float> knees = stackalloc float[bufferSize];
            Span<float> makeups = stackalloc float[bufferSize];

            // Fallbacks for when an animated parameter samples to NaN/Infinity (e.g. malformed
            // KeyFrame); otherwise one non-finite value would zero out every output sample.
            EffectiveParameters fallback = ReadStaticParameters();

            int channels = input.ChannelCount;
            int sampleCount = input.SampleCount;
            var (inputChannels, outputChannels) = MapChannels(input, output);

            // Materialize the channel spans once (matching ProcessStatic) for the mono/stereo fast path;
            // the >2-channel path keeps Memory indexing where the getter cost is negligible.
            Span<float> in0 = channels <= 2 ? inputChannels[0].Span : default;
            Span<float> out0 = channels <= 2 ? outputChannels[0].Span : default;
            Span<float> in1 = channels == 2 ? inputChannels[1].Span : default;
            Span<float> out1 = channels == 2 ? outputChannels[1].Span : default;

            int processed = 0;

            // Seed with NaN so the first comparison is always unequal and coefficients compute on
            // sample 0; afterwards Exp runs only when the animated ms value changes.
            float lastAttackMs = float.NaN;
            float lastReleaseMs = float.NaN;
            float attackCoeff = 0f;
            float releaseCoeff = 0f;

            while (processed < sampleCount)
            {
                int chunkSize = Math.Min(bufferSize, sampleCount - processed);

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

                    if (channels <= 2)
                    {
                        float s0 = in0[idx];
                        float peak = AccumulatePeak(0f, s0);
                        float s1 = 0f;
                        if (channels == 2)
                        {
                            s1 = in1[idx];
                            peak = AccumulatePeak(peak, s1);
                        }

                        float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, slope);

                        out0[idx] = SanitizeOutput(s0 * gainLinear);
                        if (channels == 2)
                        {
                            out1[idx] = SanitizeOutput(s1 * gainLinear);
                        }
                    }
                    else
                    {
                        float peak = 0f;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            peak = AccumulatePeak(peak, inputChannels[ch].Span[idx]);
                        }

                        float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, slope);

                        for (int ch = 0; ch < channels; ch++)
                        {
                            float sample = inputChannels[ch].Span[idx] * gainLinear;
                            outputChannels[ch].Span[idx] = SanitizeOutput(sample);
                        }
                    }
                }

                processed += chunkSize;
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

    private EffectiveParameters ReadStaticParameters()
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(Threshold.CurrentValue, Threshold.DefaultValue, DefaultThresholdDb, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Ratio = Sanitize(Ratio.CurrentValue, Ratio.DefaultValue, DefaultRatio, MinRatio, MaxRatio, nameof(Ratio)),
            Attack = Sanitize(Attack.CurrentValue, Attack.DefaultValue, DefaultAttackMs, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Release = Sanitize(Release.CurrentValue, Release.DefaultValue, DefaultReleaseMs, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Knee = Sanitize(Knee.CurrentValue, Knee.DefaultValue, DefaultKneeDb, MinKneeDb, MaxKneeDb, nameof(Knee)),
            MakeupGain = Sanitize(MakeupGain.CurrentValue, MakeupGain.DefaultValue, DefaultMakeupGainDb, MinMakeupGainDb, MaxMakeupGainDb, nameof(MakeupGain)),
        };
    }

    private EffectiveParameters SanitizeAnimated(
        float threshold, float ratio, float attack, float release, float knee, float makeup,
        in EffectiveParameters fallback)
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(threshold, fallback.Threshold, DefaultThresholdDb, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Ratio = Sanitize(ratio, fallback.Ratio, DefaultRatio, MinRatio, MaxRatio, nameof(Ratio)),
            Attack = Sanitize(attack, fallback.Attack, DefaultAttackMs, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Release = Sanitize(release, fallback.Release, DefaultReleaseMs, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Knee = Sanitize(knee, fallback.Knee, DefaultKneeDb, MinKneeDb, MaxKneeDb, nameof(Knee)),
            MakeupGain = Sanitize(makeup, fallback.MakeupGain, DefaultMakeupGainDb, MinMakeupGainDb, MaxMakeupGainDb, nameof(MakeupGain)),
        };
    }

    // Defensive fallback if the finite detector-peak invariant is broken.
    private void RecoverEnvelopeIfNonFinite()
    {
        if (float.IsFinite(_envelopeDb)) return;
        _envelopeDb = MinDb;
        if (_loggedNonFiniteEnvelope) return;
        Logger.LogWarning(
            "Compressor envelope became non-finite (input sample produced inf/NaN); resetting to {MinDb} dB. Further occurrences will be suppressed.",
            MinDb);
        _loggedNonFiniteEnvelope = true;
    }

    // Advances the envelope follower by one sample's peak and returns the linear gain. Shared by
    // ProcessStatic and ProcessAnimated so the envelope/gain math cannot drift between the paths.
    private float NextGain(float peak, float attackCoeff, float releaseCoeff, in EffectiveParameters p, float slope)
    {
        // Silence maps to MinDb; AccumulatePeak guarantees a finite envelope input.
        float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;
        float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
        _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);
        RecoverEnvelopeIfNonFinite();

        float gainReductionDb = ComputeGainReductionDb(_envelopeDb, p.Threshold, p.Knee, slope);
        return ComputeGainLinear(gainReductionDb, p.MakeupGain);
    }

    // Combined linear gain: subtract the dB reduction, add makeup, then a single dB→linear
    // conversion. Shared by both paths so the static and animated math cannot drift apart.
    private static float ComputeGainLinear(float gainReductionDb, float makeupDb)
    {
        return AudioMath.ConvertDbToLinear(makeupDb - gainReductionDb);
    }

    // Soft-knee gain computer (Reece/Giannoulis): when kneeDb > 0, a C¹-continuous quadratic blends
    // from no compression to the full `slope * diff` line over a kneeDb-wide region around the
    // threshold. kneeDb == 0 collapses to the hard-knee formula.
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
                // Quadratic across the knee: 0 at -halfKnee, slope * halfKnee at +halfKnee, with
                // matching derivatives at both ends so the curve stays smooth.
                float x = diff + halfKnee;
                return slope * x * x / (2f * kneeDb);
            }
            return slope * diff;
        }

        return envelopeDb > thresholdDb ? slope * (envelopeDb - thresholdDb) : 0f;
    }

    protected override void ResetDspState()
    {
        _envelopeDb = MinDb;
    }

    protected override void ResetDiagnostics()
    {
        base.ResetDiagnostics();
        _loggedNonFiniteEnvelope = false;
    }

    // readonly + init-only: built once via object initializer, then only read (passed `in`), so
    // immutability is intentional.
    private readonly struct EffectiveParameters
    {
        public float Threshold { get; init; }
        public float Ratio { get; init; }
        public float Attack { get; init; }
        public float Release { get; init; }
        public float Knee { get; init; }
        public float MakeupGain { get; init; }
    }
}
