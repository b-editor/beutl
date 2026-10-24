using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class CompressorNode : DynamicsNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<CompressorNode>();

    private float _envelopeDb = MinDb;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Ratio { get; init; }

    public required IProperty<float> Attack { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Knee { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    protected override ILogger Logger => s_logger;

    protected override string DiagnosticName => "Compressor";

    // AnimationSampler does not evaluate expressions per sample, so expression-backed properties
    // remain at Value.
    protected override bool HasAnimatedParameters =>
        Threshold.GetKeyFrameAnimation() != null ||
        Ratio.GetKeyFrameAnimation() != null ||
        Attack.GetKeyFrameAnimation() != null ||
        Release.GetKeyFrameAnimation() != null ||
        Knee.GetKeyFrameAnimation() != null ||
        MakeupGain.GetKeyFrameAnimation() != null;

    protected override AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            EffectiveParameters p = ReadStaticParameters();

            var gain = new StaticGain(
                this,
                ComputeCoeff(p.Attack, context.SampleRate),
                ComputeCoeff(p.Release, context.SampleRate),
                p,
                1f - 1f / p.Ratio);

            var (inputChannels, outputChannels) = MapChannels(input, output);
            ApplyLinkedGain(inputChannels, outputChannels, input.ChannelCount, 0, input.SampleCount, ref gain);

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

            var gain = new AnimatedGain
            {
                Node = this,
                Thresholds = thresholds,
                Ratios = ratios,
                Attacks = attacks,
                Releases = releases,
                Knees = knees,
                Makeups = makeups,
                // Fallbacks for when an animated parameter samples to NaN/Infinity (e.g. malformed
                // KeyFrame); otherwise one non-finite value would zero out every output sample.
                Fallback = ReadStaticParameters(),
                SampleRate = context.SampleRate,
                // Seed with NaN so the first comparison is always unequal and coefficients compute on
                // sample 0; afterwards Exp runs only when the animated ms value changes.
                LastAttackMs = float.NaN,
                LastReleaseMs = float.NaN,
            };

            int sampleCount = input.SampleCount;
            var (inputChannels, outputChannels) = MapChannels(input, output);

            int processed = 0;
            while (processed < sampleCount)
            {
                int chunkSize = Math.Min(bufferSize, sampleCount - processed);

                var chunkRange = context.GetChunkRange(processed, chunkSize);

                context.AnimationSampler.SampleBuffer(Threshold, chunkRange, context.SampleRate, thresholds[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Ratio, chunkRange, context.SampleRate, ratios[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Attack, chunkRange, context.SampleRate, attacks[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Release, chunkRange, context.SampleRate, releases[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Knee, chunkRange, context.SampleRate, knees[..chunkSize]);
                context.AnimationSampler.SampleBuffer(MakeupGain, chunkRange, context.SampleRate, makeups[..chunkSize]);

                gain.ChunkStart = processed;
                ApplyLinkedGain(inputChannels, outputChannels, input.ChannelCount, processed, chunkSize, ref gain);

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
            Threshold = Sanitize(Threshold.Value, DefaultThresholdDb, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Ratio = Sanitize(Ratio.Value, DefaultRatio, MinRatio, MaxRatio, nameof(Ratio)),
            Attack = Sanitize(Attack.Value, DefaultAttackMs, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Release = Sanitize(Release.Value, DefaultReleaseMs, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Knee = Sanitize(Knee.Value, DefaultKneeDb, MinKneeDb, MaxKneeDb, nameof(Knee)),
            MakeupGain = Sanitize(MakeupGain.Value, DefaultMakeupGainDb, MinMakeupGainDb, MaxMakeupGainDb, nameof(MakeupGain)),
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

    // Advances the envelope follower by one sample's peak and returns the linear gain. Shared by
    // ProcessStatic and ProcessAnimated so the envelope/gain math cannot drift between the paths.
    private float NextGain(float peak, float attackCoeff, float releaseCoeff, in EffectiveParameters p, float slope)
    {
        // Silence maps to MinDb; AccumulatePeak guarantees a finite envelope input.
        float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;
        float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
        _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);

        float gainReductionDb = ComputeGainReductionDb(_envelopeDb, p.Threshold, p.Knee, slope);
        return AudioMath.ConvertDbToLinear(p.MakeupGain - gainReductionDb);
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

    private readonly struct StaticGain(
        CompressorNode node, float attackCoeff, float releaseCoeff, EffectiveParameters p, float slope) : ILinkedGain
    {
        public float Next(int index, float peak) => node.NextGain(peak, attackCoeff, releaseCoeff, p, slope);
    }

    private ref struct AnimatedGain : ILinkedGain
    {
        public CompressorNode Node;
        public Span<float> Thresholds;
        public Span<float> Ratios;
        public Span<float> Attacks;
        public Span<float> Releases;
        public Span<float> Knees;
        public Span<float> Makeups;
        public EffectiveParameters Fallback;
        public int SampleRate;
        public int ChunkStart;
        public float LastAttackMs;
        public float LastReleaseMs;
        public float AttackCoeff;
        public float ReleaseCoeff;

        public float Next(int index, float peak)
        {
            int i = index - ChunkStart;
            EffectiveParameters p = Node.SanitizeAnimated(
                Thresholds[i], Ratios[i], Attacks[i], Releases[i], Knees[i], Makeups[i], Fallback);

            if (p.Attack != LastAttackMs)
            {
                AttackCoeff = ComputeCoeff(p.Attack, SampleRate);
                LastAttackMs = p.Attack;
            }
            if (p.Release != LastReleaseMs)
            {
                ReleaseCoeff = ComputeCoeff(p.Release, SampleRate);
                LastReleaseMs = p.Release;
            }
            float slope = 1f - 1f / p.Ratio;

            return Node.NextGain(peak, AttackCoeff, ReleaseCoeff, p, slope);
        }
    }

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
