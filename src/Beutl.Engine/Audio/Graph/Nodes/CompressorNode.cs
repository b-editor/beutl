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

    // Cached per-channel buffer handles so the hot loops skip GetChannelData's checks/re-slicing.
    // Memory<float> (not Span, a ref struct); arrays reused across Process(), reallocated only on
    // channel-count change.
    private Memory<float>[]? _inputChannelCache;
    private Memory<float>[]? _outputChannelCache;

    // Latched per node instance so each non-finite warning fires only once, not per sample.
    private bool _loggedNonFiniteEnvelope;
    private bool _loggedNonFiniteSample;
    // Per-parameter so a non-finite value on one parameter does not suppress diagnostics for another.
    private readonly HashSet<string> _loggedNonFiniteParameters = new();
    // Separate once-per-parameter latch for the "finite but out-of-[Range]" case, so clamp and
    // non-finite warnings for the same parameter are not conflated.
    private readonly HashSet<string> _loggedClampedParameters = new();

    // Test-only counters (via InternalsVisibleTo) of warnings actually emitted, letting the latch
    // re-arm semantics be asserted without a logger sink.
    internal int NonFiniteSampleWarnings;
    internal int ClampWarnings;

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

        // Every path emits a fresh buffer (no pass-through), so dispose the consumed input.
        using var input = Inputs[0].Process(context);

        // A sample-rate change needs new coefficients, so treat it as a full session boundary:
        // reset both the envelope and the one-shot diagnostic latches.
        if (_lastSampleRate != context.SampleRate)
        {
            Reset();
            _lastSampleRate = context.SampleRate;
        }

        // Reset the envelope on the first call or whenever this chunk does not continue from the
        // previous one. The node is cached across Compose() calls, so without this guard stale
        // envelope state would bleed in after a seek/restart. Only DSP state resets here, NOT the
        // diagnostic latches — re-arming them on every scrub discontinuity would let a persistent
        // non-finite condition re-log per Process call. Diagnostics re-arm only on a sample-rate
        // change or an explicit Reset().
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            ResetEnvelope();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        if (input.SampleCount == 0)
        {
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

        // Expression-backed properties are deliberately not checked: AnimationSampler does not yet
        // evaluate expressions per-sample, so routing them to ProcessAnimated would just re-read the
        // same CurrentValue every iteration. FIXME: once it does (see EqualizerEffect.IsNeutral),
        // treat HasExpression as live here too, or such parameters stay frozen at build-time value.
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
                float peak = MathF.Abs(s0);
                float s1 = 0f;
                if (channels == 2)
                {
                    s1 = in1[i];
                    float a1 = MathF.Abs(s1);
                    if (a1 > peak) peak = a1;
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
                    float a = MathF.Abs(inputChannels[ch].Span[i]);
                    if (a > peak) peak = a;
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
                    float peak = MathF.Abs(s0);
                    float s1 = 0f;
                    if (channels == 2)
                    {
                        s1 = in1[idx];
                        float a1 = MathF.Abs(s1);
                        if (a1 > peak) peak = a1;
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
                        float a = MathF.Abs(inputChannels[ch].Span[idx]);
                        if (a > peak) peak = a;
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

    // Caches per-channel Memory handles, reusing the backing arrays across calls (reallocated only
    // on a channel-count change) so the hot loops avoid per-sample GetChannelData. See field comment.
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

    // Substitute fallback for NaN/Infinity, then clamp to the parameter's [Range]. The clamp stops
    // an animated value (e.g. Attack of 1e9 ms) from bypassing the declared range and freezing the
    // envelope. A finite-but-out-of-range value is a real authoring error distinct from the
    // non-finite case, so it gets its own once-per-parameter warning as a breadcrumb.
    private float Sanitize(float value, float fallback, float min, float max, string paramName)
    {
        float safe = SafeParameter(value, fallback, paramName);
        float clamped = Math.Clamp(safe, min, max);
        // Only finite values reach here as `safe != clamped` (non-finite was already replaced by the
        // in-range fallback). Latch once per parameter; re-armed only by ResetDiagnostics.
        if (clamped != safe && _loggedClampedParameters.Add(paramName))
        {
            ClampWarnings++;
            s_logger.LogWarning(
                "Compressor parameter '{Param}' value {Value} is outside its valid range [{Min}, {Max}]; clamping to {Clamped}. Further out-of-range occurrences for this parameter will be suppressed.",
                paramName, safe, min, max, clamped);
        }
        return clamped;
    }

    // Without this, one non-finite envelope sample would poison the state until the next Reset().
    // First occurrence is logged, the rest suppressed.
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
            NonFiniteSampleWarnings++;
            // Parameters are clamped and the envelope recovered, so the gain cannot overflow — a
            // non-finite sample here almost always came from upstream. Zero it to protect downstream
            // nodes and point the breadcrumb at the likely culprit.
            s_logger.LogWarning(
                "Compressor encountered a non-finite (NaN/Infinity) sample — with all parameters clamped this almost certainly originates upstream — and replaced it with 0 to protect downstream nodes. Further occurrences will be suppressed.");
            _loggedNonFiniteSample = true;
        }
        return 0f;
    }

    // Advances the envelope follower by one sample's peak and returns the linear gain. Shared by
    // ProcessStatic and ProcessAnimated so the envelope/gain math cannot drift between the paths.
    private float NextGain(float peak, float attackCoeff, float releaseCoeff, in EffectiveParameters p, float slope)
    {
        // peak == 0 (silence) collapses inputDb to MinDb. The caller's abs/max keeps peak finite
        // (a stray NaN never raises it); other non-finite envelope state is recovered below.
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

    // One-pole IIR smoothing coefficient for a 1/e settling time of `timeMs`: the envelope reaches
    // ~63% of a step change after `timeMs`. dB-domain because that better matches perceived loudness.
    private static float ComputeCoeff(float timeMs, int sampleRate)
    {
        return MathF.Exp(-1f / (timeMs * 0.001f * sampleRate));
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

    /// <summary>
    /// Resets the compressor to a clean "new render session" state: zeroes the envelope follower
    /// and re-arms the one-shot non-finite diagnostic warnings.
    /// </summary>
    /// <remarks>
    /// Do not call mid-buffer during playback — zeroing the envelope there clicks. This is for
    /// genuine session boundaries (a deliberate re-render or an orchestrator-driven stop/seek);
    /// <see cref="Process"/> already resets the envelope on a sample-rate change or time-range
    /// discontinuity. Public to match the sibling stateful nodes <c>EqualizerNode</c> / <c>DelayNode</c>.
    /// </remarks>
    public void Reset()
    {
        ResetEnvelope();
        ResetDiagnostics();
    }

    private void ResetEnvelope()
    {
        _envelopeDb = MinDb;
    }

    private void ResetDiagnostics()
    {
        _loggedNonFiniteEnvelope = false;
        _loggedNonFiniteSample = false;
        _loggedNonFiniteParameters.Clear();
        _loggedClampedParameters.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the cached handles so we do not pin the last buffers' pooled memory after
            // disposal; they are re-filled on the next Process() call.
            _inputChannelCache = null;
            _outputChannelCache = null;
        }

        base.Dispose(disposing);
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
