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

    // Per-channel handles into the current input/output buffers, cached so the per-sample hot loops
    // avoid GetChannelData's disposed/bounds checks and re-slicing on every access. Span<float>[]
    // cannot be used (Span is a ref struct), so we hold Memory<float> and materialize the span per
    // channel. The backing arrays are reused across Process() calls and only reallocated when the
    // channel count changes, mirroring EqualizerNode's channel-keyed filter cache.
    private Memory<float>[]? _inputChannelCache;
    private Memory<float>[]? _outputChannelCache;

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

        // A sample-rate change is a genuine reconfiguration (coefficients must be recomputed for
        // the new rate), so treat it as a full session boundary: reset both the envelope and the
        // one-shot diagnostic latches.
        if (_lastSampleRate != context.SampleRate)
        {
            Reset();
            _lastSampleRate = context.SampleRate;
        }

        // Reset the envelope on the very first call (no prior end recorded) and whenever the new
        // chunk does not start exactly where the previous one ended. The node instance is cached
        // across Compose() calls, so without this guard stale envelope state would bleed into the
        // first samples after a seek or stop/restart. Only the DSP state is reset here, NOT the
        // diagnostic latches: a stuttering scrubber produces many discontinuities within a single
        // session, and re-arming the one-shot warnings on every one of them would let a persistent
        // non-finite condition re-log once per Process call. Diagnostics re-arm only on a
        // sample-rate change or an explicit Reset() (e.g. a deliberate re-render).
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            ResetEnvelope();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        if (input.SampleCount == 0)
        {
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

        // Expression-backed properties are intentionally not checked here: AnimationSampler does
        // not currently evaluate expressions per-sample, so treating HasExpression as live would
        // route to ProcessAnimated and read the same CurrentValue every iteration — strictly
        // worse than ProcessStatic, which reads it once. The same root cause is documented at
        // EqualizerEffect.IsNeutral (build-time band elision), where the FIXME notes that once
        // AnimationSampler evaluates expressions per-sample, both that guard and this one must
        // be updated to treat HasExpression as live; otherwise expression-backed parameters
        // will be silently frozen at their graph-build-time value.
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
        var (inputChannels, outputChannels) = MapChannels(input, output);
        for (int i = 0; i < input.SampleCount; i++)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                float a = MathF.Abs(inputChannels[ch].Span[i]);
                if (a > peak) peak = a;
            }

            // peak == 0 (digital silence) collapses inputDb to MinDb here. The local `peak` is
            // always finite because the abs/max loop above stays at 0 when MathF.Abs(NaN) > 0
            // is false; any non-finite envelope state arriving from elsewhere is recovered by
            // RecoverEnvelopeIfNonFinite below.
            float inputDb = peak > 0f ? 20f * MathF.Log10(peak) : MinDb;
            float coeff = inputDb > _envelopeDb ? attackCoeff : releaseCoeff;
            _envelopeDb = inputDb + coeff * (_envelopeDb - inputDb);
            RecoverEnvelopeIfNonFinite();

            float gainReductionDb = ComputeGainReductionDb(_envelopeDb, p.Threshold, p.Knee, slope);
            float gainLinear = ComputeGainLinear(gainReductionDb, p.MakeupGain);

            for (int ch = 0; ch < channels; ch++)
            {
                float sample = inputChannels[ch].Span[i] * gainLinear;
                outputChannels[ch].Span[i] = SanitizeOutput(sample);
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
        var (inputChannels, outputChannels) = MapChannels(input, output);
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
                    float a = MathF.Abs(inputChannels[ch].Span[idx]);
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
                float gainLinear = ComputeGainLinear(gainReductionDb, p.MakeupGain);

                for (int ch = 0; ch < channels; ch++)
                {
                    float sample = inputChannels[ch].Span[idx] * gainLinear;
                    outputChannels[ch].Span[idx] = SanitizeOutput(sample);
                }
            }

            processed += chunkSize;
        }

        return output;
    }

    // Caches per-channel Memory handles for the current input/output buffers, reusing the backing
    // arrays across calls (only reallocated on a channel-count change). The hot loops then index
    // the cached handles instead of calling GetChannelData per sample. See the field comment.
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

    // Combined linear gain factor: the dB-domain reduction is subtracted and the makeup gain is
    // added before a single dB→linear conversion. Both ProcessStatic and ProcessAnimated share
    // this helper so the static and animated paths cannot drift out of agreement (they used to
    // apply makeup as a pre-computed `makeupLinear` multiplier vs. an in-formula addition, which
    // is mathematically equivalent but a future refactor of one branch could silently break the
    // other).
    private static float ComputeGainLinear(float gainReductionDb, float makeupDb)
    {
        return AudioMath.ConvertDbToLinear(makeupDb - gainReductionDb);
    }

    // Standard one-pole IIR smoothing coefficient for a 1/e settling time of `timeMs` at the
    // given sample rate: y[n] = x[n] + coeff * (y[n-1] - x[n]) reaches (1 - 1/e) ≈ 63% of a step
    // change after exactly `timeMs` milliseconds. Operates in dB-domain on `_envelopeDb` because
    // dB-domain peak smoothing better matches how the human ear perceives compression action.
    private static float ComputeCoeff(float timeMs, int sampleRate)
    {
        return MathF.Exp(-1f / (timeMs * 0.001f * sampleRate));
    }

    // Soft-knee gain computer (Reece/Giannoulis formulation): when `kneeDb > 0`, the gain
    // reduction curve transitions smoothly from "no compression" to the full `slope * diff`
    // line over a `kneeDb`-wide region centred on the threshold, via a quadratic that is
    // C¹-continuous at both knee boundaries. With `kneeDb == 0` this collapses to the standard
    // hard-knee formula.
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
                // Quadratic interpolation across the knee: at diff = -halfKnee returns 0, at
                // diff = +halfKnee returns slope * halfKnee, with matching derivatives at both
                // ends (= 0 below, = slope above) so the overall curve is smooth.
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
    /// Do not call this mid-buffer during continuous playback — zeroing the envelope there produces
    /// an audible click. <see cref="Reset"/> is for genuine session boundaries (a deliberate
    /// re-render, or an explicit stop/seek driven by an orchestrator). <see cref="Process"/> already
    /// resets the envelope automatically on a sample-rate change or a time-range discontinuity, so
    /// routine seeking does not require an external call. Public to match the sibling stateful nodes
    /// <c>EqualizerNode</c> and <c>DelayNode</c>, which expose <c>Reset()</c> for the same purpose.
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the cached handles so we do not keep the last processed buffers' pooled memory
            // referenced after disposal. They are re-filled at the start of the next Process() call.
            _inputChannelCache = null;
            _outputChannelCache = null;
        }

        base.Dispose(disposing);
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
