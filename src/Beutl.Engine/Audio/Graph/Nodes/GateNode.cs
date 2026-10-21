using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using static Beutl.Audio.Effects.GateParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class GateNode : DynamicsNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<GateNode>();

    private float _gateGainDb = MinDb;
    private bool _gatePrimed;
    private int _holdCounter;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Attack { get; init; }

    public required IProperty<float> Hold { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Range { get; init; }

    protected override ILogger Logger => s_logger;

    protected override string DiagnosticName => "Gate";

    // AnimationSampler does not yet evaluate expressions per sample.
    protected override bool HasAnimatedParameters =>
        Threshold.GetKeyFrameAnimation() != null ||
        Attack.GetKeyFrameAnimation() != null ||
        Hold.GetKeyFrameAnimation() != null ||
        Release.GetKeyFrameAnimation() != null ||
        Range.GetKeyFrameAnimation() != null;

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
                HoldSamples(p.Hold, context.SampleRate));

            var (inputChannels, outputChannels) = MapChannels(input, output);
            ApplyLinkedGain(inputChannels, outputChannels, input.ChannelCount, 0, input.SampleCount, ref gain);

            return output;
        }
        catch
        {
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
            Span<float> attacks = stackalloc float[bufferSize];
            Span<float> holds = stackalloc float[bufferSize];
            Span<float> releases = stackalloc float[bufferSize];
            Span<float> ranges = stackalloc float[bufferSize];

            var gain = new AnimatedGain
            {
                Node = this,
                Thresholds = thresholds,
                Attacks = attacks,
                Holds = holds,
                Releases = releases,
                Ranges = ranges,
                Fallback = ReadStaticParameters(),
                SampleRate = context.SampleRate,
                // NaN forces coefficient calculation for sample zero.
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
                context.AnimationSampler.SampleBuffer(Attack, chunkRange, context.SampleRate, attacks[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Hold, chunkRange, context.SampleRate, holds[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Release, chunkRange, context.SampleRate, releases[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Range, chunkRange, context.SampleRate, ranges[..chunkSize]);

                gain.ChunkStart = processed;
                ApplyLinkedGain(inputChannels, outputChannels, input.ChannelCount, processed, chunkSize, ref gain);

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

    private EffectiveParameters ReadStaticParameters()
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(Threshold.CurrentValue, Threshold.DefaultValue, DefaultThresholdDb, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Attack = Sanitize(Attack.CurrentValue, Attack.DefaultValue, DefaultAttackMs, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Hold = Sanitize(Hold.CurrentValue, Hold.DefaultValue, DefaultHoldMs, MinHoldMs, MaxHoldMs, nameof(Hold)),
            Release = Sanitize(Release.CurrentValue, Release.DefaultValue, DefaultReleaseMs, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Range = Sanitize(Range.CurrentValue, Range.DefaultValue, DefaultRangeDb, MinRangeDb, MaxRangeDb, nameof(Range)),
        };
    }

    private EffectiveParameters SanitizeAnimated(
        float threshold, float attack, float hold, float release, float range,
        in EffectiveParameters fallback)
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(threshold, fallback.Threshold, DefaultThresholdDb, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Attack = Sanitize(attack, fallback.Attack, DefaultAttackMs, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Hold = Sanitize(hold, fallback.Hold, DefaultHoldMs, MinHoldMs, MaxHoldMs, nameof(Hold)),
            Release = Sanitize(release, fallback.Release, DefaultReleaseMs, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Range = Sanitize(range, fallback.Range, DefaultRangeDb, MinRangeDb, MaxRangeDb, nameof(Range)),
        };
    }

    private float NextGain(float peak, float attackCoeff, float releaseCoeff, in EffectiveParameters p, int holdSamples)
    {
        // Digital silence must remain closed even at the -100 dB minimum threshold.
        float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;
        bool aboveThreshold = peak > 0f && inputDb >= p.Threshold;
        if (aboveThreshold)
        {
            _holdCounter = holdSamples;
        }

        // Read the latch before decrementing so a hold of N lasts exactly N samples.
        bool heldOpen = _holdCounter > 0;
        if (!aboveThreshold && heldOpen)
        {
            _holdCounter--;
        }

        // Seed at Range so a below-threshold lead-in starts at the configured floor.
        if (!_gatePrimed)
        {
            _gatePrimed = true;
            _gateGainDb = p.Range;
        }

        // Disabled gating must be an exact identity without smoothing artifacts.
        if (p.Range >= 0f)
        {
            _gateGainDb = 0f;
            return 1f;
        }

        float targetDb = aboveThreshold || heldOpen ? 0f : p.Range;
        float coeff = targetDb > _gateGainDb ? attackCoeff : releaseCoeff;
        // Clamped targetDb and coeff in [0, 1) keep the follower finite without a recovery clamp.
        _gateGainDb = targetDb + coeff * (_gateGainDb - targetDb);

        return AudioMath.ConvertDbToLinear(_gateGainDb);
    }

    private static int HoldSamples(float holdMs, int sampleRate)
    {
        int samples = (int)(holdMs * 0.001f * sampleRate);
        return samples < 0 ? 0 : samples;
    }

    protected override void ResetDspState()
    {
        _gateGainDb = MinDb;
        _holdCounter = 0;
        _gatePrimed = false;
    }

    private readonly struct StaticGain(
        GateNode node, float attackCoeff, float releaseCoeff, EffectiveParameters p, int holdSamples) : ILinkedGain
    {
        public float Next(int index, float peak) => node.NextGain(peak, attackCoeff, releaseCoeff, p, holdSamples);
    }

    private ref struct AnimatedGain : ILinkedGain
    {
        public GateNode Node;
        public Span<float> Thresholds;
        public Span<float> Attacks;
        public Span<float> Holds;
        public Span<float> Releases;
        public Span<float> Ranges;
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
                Thresholds[i], Attacks[i], Holds[i], Releases[i], Ranges[i], Fallback);

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
            int holdSamples = HoldSamples(p.Hold, SampleRate);

            return Node.NextGain(peak, AttackCoeff, ReleaseCoeff, p, holdSamples);
        }
    }

    private readonly struct EffectiveParameters
    {
        public float Threshold { get; init; }
        public float Attack { get; init; }
        public float Hold { get; init; }
        public float Release { get; init; }
        public float Range { get; init; }
    }
}
