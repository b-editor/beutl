using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
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
        Threshold.Animation != null ||
        Attack.Animation != null ||
        Hold.Animation != null ||
        Release.Animation != null ||
        Range.Animation != null;

    protected override AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            EffectiveParameters p = ReadStaticParameters();

            float attackCoeff = ComputeCoeff(p.Attack, context.SampleRate);
            float releaseCoeff = ComputeCoeff(p.Release, context.SampleRate);
            int holdSamples = HoldSamples(p.Hold, context.SampleRate);

            int channels = input.ChannelCount;
            int sampleCount = input.SampleCount;
            var (inputChannels, outputChannels) = MapChannels(input, output);

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

                    float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, holdSamples);

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

                    float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, holdSamples);

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

            EffectiveParameters fallback = ReadStaticParameters();

            int channels = input.ChannelCount;
            int sampleCount = input.SampleCount;
            var (inputChannels, outputChannels) = MapChannels(input, output);

            Span<float> in0 = channels <= 2 ? inputChannels[0].Span : default;
            Span<float> out0 = channels <= 2 ? outputChannels[0].Span : default;
            Span<float> in1 = channels == 2 ? inputChannels[1].Span : default;
            Span<float> out1 = channels == 2 ? outputChannels[1].Span : default;

            int processed = 0;

            // NaN forces coefficient calculation for sample zero.
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
                context.AnimationSampler.SampleBuffer(Attack, chunkRange, context.SampleRate, attacks[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Hold, chunkRange, context.SampleRate, holds[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Release, chunkRange, context.SampleRate, releases[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Range, chunkRange, context.SampleRate, ranges[..chunkSize]);

                for (int i = 0; i < chunkSize; i++)
                {
                    int idx = processed + i;

                    EffectiveParameters p = SanitizeAnimated(
                        thresholds[i], attacks[i], holds[i], releases[i], ranges[i], fallback);

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
                    int holdSamples = HoldSamples(p.Hold, context.SampleRate);

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

                        float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, holdSamples);

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

                        float gainLinear = NextGain(peak, attackCoeff, releaseCoeff, p, holdSamples);

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

    private readonly struct EffectiveParameters
    {
        public float Threshold { get; init; }
        public float Attack { get; init; }
        public float Hold { get; init; }
        public float Release { get; init; }
        public float Range { get; init; }
    }
}
