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

    // Gate gain in dB, smoothed toward 0 (open) or the Range floor (closed). Starts fully closed so a
    // silent lead-in stays quiet until the signal crosses the threshold.
    private float _gateGainDb = MinDb;
    // Samples remaining before a gate that has dropped below threshold begins to release.
    private int _holdCounter;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    // Cached per-channel buffer handles so the hot loops skip GetChannelData's checks/re-slicing;
    // arrays reused across Process(), reallocated only on a channel-count change.
    private Memory<float>[]? _inputChannelCache;
    private Memory<float>[]? _outputChannelCache;

    // Latched per node instance so each non-finite warning fires only once, not per sample.
    private bool _loggedNonFiniteGain;
    private bool _loggedNonFiniteSample;
    private readonly HashSet<string> _loggedNonFiniteParameters = new();
    private readonly HashSet<string> _loggedClampedParameters = new();

    // Test-only counters (via InternalsVisibleTo) of warnings actually emitted.
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

        if (_lastSampleRate != context.SampleRate)
        {
            Reset();
            _lastSampleRate = context.SampleRate;
        }

        // Reset the gate on the first call or whenever this chunk does not continue from the previous
        // one; the node is cached across Compose() calls, so stale gate state would otherwise bleed in
        // after a seek/restart. Only DSP state resets here, not the diagnostic latches.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            ResetGate();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        if (input.SampleCount == 0)
        {
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

        // Expression-backed properties are deliberately not treated as animated: AnimationSampler does
        // not yet evaluate expressions per-sample, so routing them through ProcessAnimated would just
        // re-read the same CurrentValue every iteration.
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
                    float peak = MathF.Abs(s0);
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
                        float peak = MathF.Abs(s0);
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
            Threshold = Sanitize(Threshold.CurrentValue, Threshold.DefaultValue, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Attack = Sanitize(Attack.CurrentValue, Attack.DefaultValue, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Hold = Sanitize(Hold.CurrentValue, Hold.DefaultValue, MinHoldMs, MaxHoldMs, nameof(Hold)),
            Release = Sanitize(Release.CurrentValue, Release.DefaultValue, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Range = Sanitize(Range.CurrentValue, Range.DefaultValue, MinRangeDb, MaxRangeDb, nameof(Range)),
        };
    }

    private EffectiveParameters SanitizeAnimated(
        float threshold, float attack, float hold, float release, float range,
        in EffectiveParameters fallback)
    {
        return new EffectiveParameters
        {
            Threshold = Sanitize(threshold, fallback.Threshold, MinThresholdDb, MaxThresholdDb, nameof(Threshold)),
            Attack = Sanitize(attack, fallback.Attack, MinAttackMs, MaxAttackMs, nameof(Attack)),
            Hold = Sanitize(hold, fallback.Hold, MinHoldMs, MaxHoldMs, nameof(Hold)),
            Release = Sanitize(release, fallback.Release, MinReleaseMs, MaxReleaseMs, nameof(Release)),
            Range = Sanitize(range, fallback.Range, MinRangeDb, MaxRangeDb, nameof(Range)),
        };
    }

    // Substitute fallback for NaN/Infinity, then clamp to the parameter's [Range]. A finite-but-out-of-
    // range value is a real authoring error distinct from the non-finite case, so it gets its own
    // once-per-parameter warning.
    private float Sanitize(float value, float fallback, float min, float max, string paramName)
    {
        float safe = SafeParameter(value, fallback, paramName);
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

    // Without this, one non-finite gain sample would poison the state until the next Reset().
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

    private float SafeParameter(float value, float fallback, string paramName)
    {
        if (float.IsFinite(value)) return value;
        if (_loggedNonFiniteParameters.Add(paramName))
        {
            s_logger.LogWarning(
                "Gate parameter '{Param}' produced a non-finite value; falling back to {Fallback}. Further occurrences for this parameter will be suppressed.",
                paramName, fallback);
        }
        return fallback;
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

    // Advances the gate one sample and returns the linear gain. Shared by ProcessStatic and
    // ProcessAnimated so the gate/hold math cannot drift between the paths.
    private float NextGain(float peak, float attackCoeff, float releaseCoeff, in EffectiveParameters p, int holdSamples)
    {
        float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;
        bool aboveThreshold = inputDb >= p.Threshold;
        if (aboveThreshold)
        {
            _holdCounter = holdSamples;
        }
        else if (_holdCounter > 0)
        {
            _holdCounter--;
        }

        // Open (0 dB) while the signal is above threshold or the hold timer keeps it latched; else
        // fall to the Range floor. Attack smooths the rise (opening), release smooths the fall.
        float targetDb = aboveThreshold || _holdCounter > 0 ? 0f : p.Range;
        float coeff = targetDb > _gateGainDb ? attackCoeff : releaseCoeff;
        _gateGainDb = targetDb + coeff * (_gateGainDb - targetDb);
        RecoverGainIfNonFinite();

        return AudioMath.ConvertDbToLinear(_gateGainDb);
    }

    // One-pole IIR smoothing coefficient for a 1/e settling time of `timeMs`.
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
    /// Resets the gate to a clean "new render session" state: closes the gate, clears the hold timer,
    /// and re-arms the one-shot non-finite diagnostic warnings.
    /// </summary>
    /// <remarks>
    /// Do not call mid-buffer during playback — closing the gate there clicks. This is for genuine
    /// session boundaries; <see cref="Process"/> already resets the gate on a sample-rate change or
    /// time-range discontinuity. Public to match the sibling stateful nodes.
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
