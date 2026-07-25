using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.Audio.Effects.GateParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class GateNode : AudioNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<GateNode>();

    private const float MinDb = -100f;

    // Independent TimeSpan rounding can shift adjacent sample boundaries by one tick.
    private const long TimestampQuantizationToleranceTicks = 1;

    private float _gateGainDb = MinDb;
    private bool _gatePrimed;
    private int _holdCounter;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    // Reuse channel views to avoid checks and slicing in the sample loops.
    private Memory<float>[]? _inputChannelCache;
    private Memory<float>[]? _outputChannelCache;

    // Diagnostic warnings are emitted once per node, not once per sample.
    private bool _loggedNonFiniteGain;
    private bool _loggedNonFiniteSample;
    private readonly HashSet<string> _loggedNonFiniteParameters = new();
    private readonly HashSet<string> _loggedClampedParameters = new();

    internal int NonFiniteSampleWarnings;
    internal int ClampWarnings;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Attack { get; init; }

    public required IProperty<float> Hold { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Range { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException(
                $"Gate node requires exactly one input but got {Inputs.Count}.");

        using var input = Inputs[0].Process(context);

        // A mismatch would mislabel samples because this node does not resample.
        if (input.SampleRate != context.SampleRate)
            throw new InvalidOperationException(
                $"Gate node: sample rate mismatch. context={context.SampleRate}, input={input.SampleRate}.");

        if (_lastSampleRate != context.SampleRate)
        {
            Reset();
            _lastSampleRate = context.SampleRate;
        }

        // Cached nodes must not carry DSP state across seeks or restarts.
        if (!_lastTimeRangeEnd.HasValue || !IsTimestampContiguous(_lastTimeRangeEnd.Value, context.TimeRange.Start))
        {
            ResetGate();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        if (input.SampleCount == 0)
        {
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

        // AnimationSampler does not yet evaluate expressions per sample.
        bool hasAnimation = Threshold.Animation != null ||
                            Attack.Animation != null ||
                            Hold.Animation != null ||
                            Release.Animation != null ||
                            Range.Animation != null;

        if (!hasAnimation)
        {
            return ProcessStatic(input, context);
        }

        return ProcessAnimated(input, context);
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
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
                    // Comparisons against NaN stay false, preserving a finite channel's peak.
                    float peak = 0f;
                    float a0 = MathF.Abs(s0);
                    if (a0 > peak) peak = a0;
                    float s1 = 0f;
                    if (channels == 2)
                    {
                        s1 = in1[i];
                        float a1 = MathF.Abs(s1);
                        if (a1 > peak) peak = a1;
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
                        float a = MathF.Abs(inputChannels[ch].Span[i]);
                        if (a > peak) peak = a;
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

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
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
                        // Comparisons against NaN stay false, preserving a finite channel's peak.
                        float peak = 0f;
                        float a0 = MathF.Abs(s0);
                        if (a0 > peak) peak = a0;
                        float s1 = 0f;
                        if (channels == 2)
                        {
                            s1 = in1[idx];
                            float a1 = MathF.Abs(s1);
                            if (a1 > peak) peak = a1;
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
                            float a = MathF.Abs(inputChannels[ch].Span[idx]);
                            if (a > peak) peak = a;
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

    private (Memory<float>[] Inputs, Memory<float>[] Outputs) MapChannels(AudioBuffer input, AudioBuffer output)
    {
        int channels = input.ChannelCount;
        if (_inputChannelCache is null || _inputChannelCache.Length != channels)
        {
            _inputChannelCache = new Memory<float>[channels];
            _outputChannelCache = new Memory<float>[channels];
        }

        for (int ch = 0; ch < channels; ch++)
        {
            _inputChannelCache[ch] = input.GetChannelMemory(ch);
            _outputChannelCache![ch] = output.GetChannelMemory(ch);
        }

        return (_inputChannelCache, _outputChannelCache!);
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

    // Non-finite and out-of-range values have distinct once-per-parameter warnings.
    private float Sanitize(float value, float fallback, float declaredDefault, float min, float max, string paramName)
    {
        float safe = SafeParameter(value, fallback, declaredDefault, paramName);
        float clamped = Math.Clamp(safe, min, max);
        if (clamped != safe && _loggedClampedParameters.Add(paramName))
        {
            ClampWarnings++;
            s_logger.LogWarning(
                "Gate parameter '{Param}' value {Value} is outside its valid range [{Min}, {Max}]; clamping to {Clamped}. Further out-of-range occurrences for this parameter will be suppressed.",
                paramName, safe, min, max, clamped);
        }
        return clamped;
    }

    // A non-finite gain would otherwise poison the state until Reset().
    private void RecoverGainIfNonFinite()
    {
        if (float.IsFinite(_gateGainDb)) return;
        _gateGainDb = MinDb;
        if (_loggedNonFiniteGain) return;
        s_logger.LogWarning(
            "Gate gain became non-finite (input sample produced inf/NaN); resetting to {MinDb} dB. Further occurrences will be suppressed.",
            MinDb);
        _loggedNonFiniteGain = true;
    }

    private float SafeParameter(float value, float fallback, float declaredDefault, string paramName)
    {
        if (float.IsFinite(value)) return value;
        // IProperty implementations may expose non-finite defaults, which Math.Clamp cannot recover.
        float safeFallback = float.IsFinite(fallback) ? fallback : declaredDefault;
        if (_loggedNonFiniteParameters.Add(paramName))
        {
            s_logger.LogWarning(
                "Gate parameter '{Param}' produced a non-finite value; falling back to {Fallback}. Further occurrences for this parameter will be suppressed.",
                paramName, safeFallback);
        }
        return safeFallback;
    }

    private float SanitizeOutput(float sample)
    {
        if (float.IsFinite(sample)) return sample;
        if (!_loggedNonFiniteSample)
        {
            NonFiniteSampleWarnings++;
            s_logger.LogWarning(
                "Gate encountered a non-finite (NaN/Infinity) sample — with all parameters clamped this almost certainly originates upstream — and replaced it with 0 to protect downstream nodes. Further occurrences will be suppressed.");
            _loggedNonFiniteSample = true;
        }
        return 0f;
    }

    // Shared by static and animated processing to keep gate state transitions identical.
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
        _gateGainDb = targetDb + coeff * (_gateGainDb - targetDb);
        RecoverGainIfNonFinite();

        return AudioMath.ConvertDbToLinear(_gateGainDb);
    }

    // One-pole IIR coefficient with a 1/e settling time of timeMs.
    private static float ComputeCoeff(float timeMs, int sampleRate)
    {
        return MathF.Exp(-1f / (timeMs * 0.001f * sampleRate));
    }

    private static int HoldSamples(float holdMs, int sampleRate)
    {
        int samples = (int)(holdMs * 0.001f * sampleRate);
        return samples < 0 ? 0 : samples;
    }

    /// <summary>
    /// Resets all gate state for a new render session.
    /// </summary>
    /// <remarks>
    /// Do not call during playback because closing the gate mid-buffer clicks.
    /// <see cref="Process"/> handles sample-rate changes and time-range discontinuities.
    /// </remarks>
    public void Reset()
    {
        ResetGate();
        ResetDiagnostics();
    }

    private void ResetGate()
    {
        _gateGainDb = MinDb;
        _holdCounter = 0;
        _gatePrimed = false;
    }

    private static bool IsTimestampContiguous(TimeSpan previousEnd, TimeSpan nextStart)
    {
        long difference = nextStart.Ticks - previousEnd.Ticks;
        return difference is >= -TimestampQuantizationToleranceTicks and <= TimestampQuantizationToleranceTicks;
    }

    private void ResetDiagnostics()
    {
        _loggedNonFiniteGain = false;
        _loggedNonFiniteSample = false;
        _loggedNonFiniteParameters.Clear();
        _loggedClampedParameters.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inputChannelCache = null;
            _outputChannelCache = null;
        }

        base.Dispose(disposing);
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
