using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Audio.Graph.Nodes;

public sealed class LimiterNode : AudioNode
{
    // 4 × 1024 × sizeof(float) = 16 KiB per ProcessAnimated call — well within
    // a thread's default stack budget while still amortizing the per-chunk
    // animation sampling overhead.
    private const int AnimationChunkSize = 1024;

    private static readonly ILogger s_logger = Log.CreateLogger<LimiterNode>();

    private CircularBuffer<float>[]? _delayLines;
    private CircularBuffer<float>? _peakBuffer;
    private int _maxLookaheadSamples;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;
    private float _currentGain = 1f;

    // Latched warning flags — keep audio-rate logging from spamming the sink.
    // Cleared whenever Reset() runs so a new discontinuity can re-surface the warning.
    private bool _warnedNonFiniteParameter;
    private bool _warnedNonFiniteInputSample;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Lookahead { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException(
                $"LimiterNode requires exactly one input but has {Inputs.Count}.");

        var input = Inputs[0].Process(context)
            ?? throw new InvalidOperationException("LimiterNode: upstream Process returned null.");

        if (input.SampleRate != context.SampleRate)
            throw new InvalidOperationException(
                $"LimiterNode: sample rate mismatch. context={context.SampleRate}, input={input.SampleRate}.");

        if (input.SampleCount == 0)
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);

        if (_delayLines == null || _peakBuffer == null
            || _lastSampleRate != context.SampleRate
            || _delayLines.Length != input.ChannelCount)
        {
            InitializeBuffers(context.SampleRate, input.ChannelCount);
            _lastSampleRate = context.SampleRate;
        }

        // The node instance is cached and reused across chunks. When the next chunk does not
        // start exactly where the previous one ended (seek, loop, edit, restart) we must drop
        // the delay line and gain state — otherwise audio from the previous segment would leak
        // into the first lookahead-window worth of output samples.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            if (_lastTimeRangeEnd.HasValue)
            {
                s_logger.LogTrace(
                    "LimiterNode: chunk discontinuity (last end={Last}, new start={Start}); resetting state.",
                    _lastTimeRangeEnd, context.TimeRange.Start);
            }

            Reset();
        }

        bool hasAnimation = Threshold.Animation != null
                            || Release.Animation != null
                            || Lookahead.Animation != null
                            || MakeupGain.Animation != null;

        var output = hasAnimation
            ? ProcessAnimated(input, context)
            : ProcessStatic(input, context);

        // Update only on success: if Process throws mid-buffer, _currentGain and the delay line
        // may be partially mutated. Leaving _lastTimeRangeEnd untouched routes the next call
        // through Reset() instead of inheriting that partial state.
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        return output;
    }

    private void InitializeBuffers(int sampleRate, int channelCount)
    {
        // Tear the previous state down first and null the fields immediately so a throw inside
        // the construction loop below cannot leave us referencing half-initialized buffers.
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Dispose();
            }

            _delayLines = null;
        }

        _peakBuffer?.Dispose();
        _peakBuffer = null;

        // +1 because CircularBuffer.Read(samplesBack) returns silence when samplesBack >= length,
        // so the requested length must be strictly greater than the maximum lookaheadSamples we
        // will ever clamp to (LimiterEffect.MaxLookaheadMs · sampleRate). The buffer rounds this
        // up to the next power of two internally — the +1 is for the read-bounds check, not the
        // rounding.
        int max = Math.Max(1, (int)(LimiterEffect.MaxLookaheadMs / 1000f * sampleRate) + 1);

        var lines = new CircularBuffer<float>[channelCount];
        try
        {
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = new CircularBuffer<float>(max);
            }

            _peakBuffer = new CircularBuffer<float>(max);
        }
        catch
        {
            foreach (var l in lines)
            {
                l?.Dispose();
            }

            _peakBuffer?.Dispose();
            _peakBuffer = null;
            throw;
        }

        _maxLookaheadSamples = max;
        _delayLines = lines;
        _currentGain = 1f;
    }

    // Math.Clamp does not coerce NaN nor ±Infinity to the bounds — both would poison
    // _currentGain permanently if they slipped through. Substituting a safe fallback here keeps
    // the DSP stable; the caller logs once when the substitution actually fires.
    private float ClampFinite(float value, float min, float max, float fallback, string parameterName)
    {
        if (float.IsFinite(value))
            return Math.Clamp(value, min, max);

        if (!_warnedNonFiniteParameter)
        {
            s_logger.LogWarning(
                "LimiterNode: non-finite {Parameter}={Value}; substituting {Fallback}. Likely an animation/binding bug upstream.",
                parameterName, value, fallback);
            _warnedNonFiniteParameter = true;
        }

        return fallback;
    }

    private readonly record struct DerivedCoefficients(
        float ThresholdLin,
        float MakeupLin,
        int LookaheadSamples,
        float ReleaseCoef);

    private DerivedCoefficients Derive(float thresholdDbRaw, float releaseMsRaw, float lookaheadMsRaw, float makeupDbRaw, int sampleRate)
    {
        float thresholdDb = ClampFinite(thresholdDbRaw, LimiterEffect.MinThresholdDb, LimiterEffect.MaxThresholdDb, LimiterEffect.MaxThresholdDb, nameof(Threshold));
        float releaseMs = ClampFinite(releaseMsRaw, LimiterEffect.MinReleaseMs, LimiterEffect.MaxReleaseMs, LimiterEffect.MinReleaseMs, nameof(Release));
        float lookaheadMs = ClampFinite(lookaheadMsRaw, LimiterEffect.MinLookaheadMs, LimiterEffect.MaxLookaheadMs, LimiterEffect.MinLookaheadMs, nameof(Lookahead));
        float makeupDb = ClampFinite(makeupDbRaw, LimiterEffect.MinMakeupGainDb, LimiterEffect.MaxMakeupGainDb, 0f, nameof(MakeupGain));

        return new DerivedCoefficients(
            ThresholdLin: AudioMath.ConvertDbToLinear(thresholdDb),
            MakeupLin: AudioMath.ConvertDbToLinear(makeupDb),
            LookaheadSamples: Math.Clamp((int)(lookaheadMs / 1000f * sampleRate), 0, _maxLookaheadSamples - 1),
            ReleaseCoef: MathF.Exp(-1f / (releaseMs * 0.001f * sampleRate)));
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var c = Derive(Threshold.CurrentValue, Release.CurrentValue, Lookahead.CurrentValue, MakeupGain.CurrentValue, context.SampleRate);

        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        for (int i = 0; i < input.SampleCount; i++)
        {
            ProcessSingleSample(input, output, i, c.ThresholdLin, c.MakeupLin, c.LookaheadSamples, c.ReleaseCoef);
        }

        return output;
    }

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        Span<float> thresholds = stackalloc float[Math.Min(input.SampleCount, AnimationChunkSize)];
        Span<float> releases = stackalloc float[Math.Min(input.SampleCount, AnimationChunkSize)];
        Span<float> lookaheads = stackalloc float[Math.Min(input.SampleCount, AnimationChunkSize)];
        Span<float> makeups = stackalloc float[Math.Min(input.SampleCount, AnimationChunkSize)];

        int processed = 0;
        while (processed < input.SampleCount)
        {
            int chunkSize = Math.Min(thresholds.Length, input.SampleCount - processed);

            var chunkStart = context.GetTimeForSample(processed);
            var chunkEnd = context.GetTimeForSample(processed + chunkSize);
            var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

            context.AnimationSampler.SampleBuffer(Threshold, chunkRange, context.SampleRate, thresholds[..chunkSize]);
            context.AnimationSampler.SampleBuffer(Release, chunkRange, context.SampleRate, releases[..chunkSize]);
            context.AnimationSampler.SampleBuffer(Lookahead, chunkRange, context.SampleRate, lookaheads[..chunkSize]);
            context.AnimationSampler.SampleBuffer(MakeupGain, chunkRange, context.SampleRate, makeups[..chunkSize]);

            for (int i = 0; i < chunkSize; i++)
            {
                var c = Derive(thresholds[i], releases[i], lookaheads[i], makeups[i], context.SampleRate);
                ProcessSingleSample(input, output, processed + i, c.ThresholdLin, c.MakeupLin, c.LookaheadSamples, c.ReleaseCoef);
            }

            processed += chunkSize;
        }

        return output;
    }

    private void ProcessSingleSample(
        AudioBuffer input,
        AudioBuffer output,
        int sampleIndex,
        float thresholdLin,
        float makeupLin,
        int lookaheadSamples,
        float releaseCoef)
    {
        int channelCount = _delayLines!.Length;

        // Channel-linked peak detection: take max(|s_ch|) across all channels so that a
        // single shared gain is applied to every channel — preserves inter-channel phase.
        //
        // NaN/Infinity input samples are coerced to 0 here. Without this guard:
        //   - NaN written into the delay line passes straight through to the output.
        //   - Inf forces currentPeak → Inf, then targetGain = thresholdLin / Inf = 0,
        //     and finally `delayed * 0` = `Inf * 0` = NaN once the gain reduction kicks in.
        // We log at most once per Reset() so an upstream bug surfaces without flooding the sink.
        float currentPeak = 0f;
        for (int ch = 0; ch < channelCount; ch++)
        {
            float s = input.GetChannelData(ch)[sampleIndex];
            if (!float.IsFinite(s))
            {
                if (!_warnedNonFiniteInputSample)
                {
                    s_logger.LogWarning(
                        "LimiterNode: non-finite input sample at channel={Channel}, index={Index}, value={Value}. Likely upstream bug; coercing to 0.",
                        ch, sampleIndex, s);
                    _warnedNonFiniteInputSample = true;
                }

                s = 0f;
            }

            float abs = MathF.Abs(s);
            if (abs > currentPeak)
                currentPeak = abs;

            _delayLines[ch].Write(s);
        }

        _peakBuffer!.Write(currentPeak);

        // Worst-case future peak that the sample currently exiting the delay line is about to
        // face — feeds the gain calculation so the reduction is in place before the peak arrives.
        float windowPeak = 0f;
        int windowSize = lookaheadSamples + 1;
        for (int j = 0; j < windowSize; j++)
        {
            float v = _peakBuffer.Read(j);
            if (v > windowPeak)
                windowPeak = v;
        }

        float targetGain;
        if (windowPeak > thresholdLin && windowPeak > 0f)
        {
            targetGain = thresholdLin / windowPeak;
        }
        else
        {
            targetGain = 1f;
        }

        // With non-zero lookahead the reduction is applied before the offending sample reaches
        // the output, so a hard attack stays transparent. With lookahead=0 this degrades to a
        // hard-clipper-style limiter (still correct, just less transparent).
        if (targetGain < _currentGain)
        {
            _currentGain = targetGain;
        }
        else
        {
            _currentGain = targetGain + (_currentGain - targetGain) * releaseCoef;
        }

        float finalGain = _currentGain * makeupLin;

        for (int ch = 0; ch < channelCount; ch++)
        {
            float delayed = _delayLines[ch].Read(lookaheadSamples);
            output.GetChannelData(ch)[sampleIndex] = delayed * finalGain;
        }
    }

    /// <summary>
    /// Clears delay-line state and resets the internal gain to unity. Process() invokes this
    /// automatically on chunk discontinuity, so external callers do not normally need to call it.
    /// </summary>
    public void Reset()
    {
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Clear();
            }
        }

        _peakBuffer?.Clear();
        _currentGain = 1f;
        _warnedNonFiniteParameter = false;
        _warnedNonFiniteInputSample = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_delayLines != null)
            {
                foreach (var line in _delayLines)
                {
                    line.Dispose();
                }

                _delayLines = null;
            }

            _peakBuffer?.Dispose();
            _peakBuffer = null;
        }

        base.Dispose(disposing);
    }
}
