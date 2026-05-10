using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class LimiterNode : AudioNode
{
    private const int AnimationChunkSize = 1024;

    private CircularBuffer<float>[]? _delayLines;
    private CircularBuffer<float>? _peakBuffer;
    private int _maxLookaheadSamples;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;
    private float _currentGain = 1f;

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

        // The node instance is cached across Compose() calls, so when the next chunk does not
        // continue directly from the previous one (seek, stop/restart) we must drop the
        // delay-line and gain state — otherwise audio from the previous segment would leak
        // into the first lookahead-window worth of output samples.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            Reset();
        }

        bool hasAnimation = Threshold.Animation != null
                            || Release.Animation != null
                            || Lookahead.Animation != null
                            || MakeupGain.Animation != null;

        var output = hasAnimation
            ? ProcessAnimated(input, context)
            : ProcessStatic(input, context);

        // Update only on success so that a thrown exception lets the next call detect the
        // discontinuity and reset, instead of silently inheriting corrupt state.
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        return output;
    }

    private void InitializeBuffers(int sampleRate, int channelCount)
    {
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Dispose();
            }
        }

        _peakBuffer?.Dispose();

        // +1 because Read(lookaheadSamples) needs lookaheadSamples + 1 valid slots when
        // lookaheadSamples is at its maximum (LimiterEffect.MaxLookaheadMs · sampleRate).
        _maxLookaheadSamples = Math.Max(1, (int)(LimiterEffect.MaxLookaheadMs / 1000f * sampleRate) + 1);
        _delayLines = new CircularBuffer<float>[channelCount];
        for (int i = 0; i < _delayLines.Length; i++)
        {
            _delayLines[i] = new CircularBuffer<float>(_maxLookaheadSamples);
        }

        _peakBuffer = new CircularBuffer<float>(_maxLookaheadSamples);
        _currentGain = 1f;
    }

    // Math.Clamp propagates NaN — wrap to fall back to a safe value before NaN can poison
    // _currentGain (a NaN there persists across every subsequent sample).
    private static float ClampFinite(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        float thresholdDb = ClampFinite(Threshold.CurrentValue, LimiterEffect.MinThresholdDb, LimiterEffect.MaxThresholdDb, LimiterEffect.MaxThresholdDb);
        float releaseMs = ClampFinite(Release.CurrentValue, LimiterEffect.MinReleaseMs, LimiterEffect.MaxReleaseMs, LimiterEffect.MinReleaseMs);
        float lookaheadMs = ClampFinite(Lookahead.CurrentValue, LimiterEffect.MinLookaheadMs, LimiterEffect.MaxLookaheadMs, LimiterEffect.MinLookaheadMs);
        float makeupDb = ClampFinite(MakeupGain.CurrentValue, LimiterEffect.MinMakeupGainDb, LimiterEffect.MaxMakeupGainDb, 0f);

        float thresholdLin = AudioMath.ConvertDbToLinear(thresholdDb);
        float makeupLin = AudioMath.ConvertDbToLinear(makeupDb);
        int lookaheadSamples = Math.Clamp((int)(lookaheadMs / 1000f * context.SampleRate), 0, _maxLookaheadSamples - 1);
        float releaseCoef = MathF.Exp(-1f / (releaseMs * 0.001f * context.SampleRate));

        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        for (int i = 0; i < input.SampleCount; i++)
        {
            ProcessSingleSample(input, output, i, thresholdLin, makeupLin, lookaheadSamples, releaseCoef);
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
                float thresholdDb = ClampFinite(thresholds[i], LimiterEffect.MinThresholdDb, LimiterEffect.MaxThresholdDb, LimiterEffect.MaxThresholdDb);
                float releaseMs = ClampFinite(releases[i], LimiterEffect.MinReleaseMs, LimiterEffect.MaxReleaseMs, LimiterEffect.MinReleaseMs);
                float lookaheadMs = ClampFinite(lookaheads[i], LimiterEffect.MinLookaheadMs, LimiterEffect.MaxLookaheadMs, LimiterEffect.MinLookaheadMs);
                float makeupDb = ClampFinite(makeups[i], LimiterEffect.MinMakeupGainDb, LimiterEffect.MaxMakeupGainDb, 0f);

                float thresholdLin = AudioMath.ConvertDbToLinear(thresholdDb);
                float makeupLin = AudioMath.ConvertDbToLinear(makeupDb);
                int lookaheadSamples = Math.Clamp((int)(lookaheadMs / 1000f * context.SampleRate), 0, _maxLookaheadSamples - 1);
                float releaseCoef = MathF.Exp(-1f / (releaseMs * 0.001f * context.SampleRate));

                ProcessSingleSample(input, output, processed + i, thresholdLin, makeupLin, lookaheadSamples, releaseCoef);
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
        // NaN/Infinity samples from upstream are coerced to 0 here: writing them into the
        // delay line would either pass NaN straight through to the output or produce
        // Inf*0 = NaN once the gain reduction kicks in.
        float currentPeak = 0f;
        for (int ch = 0; ch < channelCount; ch++)
        {
            float s = input.GetChannelData(ch)[sampleIndex];
            if (!float.IsFinite(s)) s = 0f;

            float abs = MathF.Abs(s);
            if (abs > currentPeak)
                currentPeak = abs;

            _delayLines[ch].Write(s);
        }

        _peakBuffer!.Write(currentPeak);

        // Maximum peak that has not yet exited the delay line. This is the worst-case peak
        // the sample being read from the delay line is allowed to encounter.
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

        // Instant attack is safe here: the lookahead delay means we apply the reduction
        // before the offending sample reaches the output, avoiding distortion.
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
    /// Clears delay-line state and resets the internal gain to unity.
    /// Process() invokes this automatically on chunk discontinuity, so external
    /// callers do not normally need to call it.
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
