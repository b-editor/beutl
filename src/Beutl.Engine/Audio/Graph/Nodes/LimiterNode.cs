using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class LimiterNode : AudioNode
{
    private const float MaxLookaheadMs = 20f;
    private const float MinReleaseMs = 1f;

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
            throw new InvalidOperationException("Limiter node requires exactly one input.");

        var input = Inputs[0].Process(context);

        if (_delayLines == null || _peakBuffer == null
            || _lastSampleRate != context.SampleRate
            || _delayLines.Length != input.ChannelCount)
        {
            InitializeBuffers(context.SampleRate, input.ChannelCount);
            _lastSampleRate = context.SampleRate;
        }

        // Reset whenever the chunk does not continue directly from the previous one, because the
        // node instance is cached across Compose() calls and stale lookahead/gain state would
        // otherwise bleed into the first samples after a seek or stop/restart.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            Reset();
        }

        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        bool hasAnimation = Threshold.Animation != null
                            || Release.Animation != null
                            || Lookahead.Animation != null
                            || MakeupGain.Animation != null;

        return hasAnimation
            ? ProcessAnimated(input, context)
            : ProcessStatic(input, context);
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

        _maxLookaheadSamples = Math.Max(1, (int)(MaxLookaheadMs / 1000f * sampleRate) + 1);
        _delayLines = new CircularBuffer<float>[channelCount];
        for (int i = 0; i < _delayLines.Length; i++)
        {
            _delayLines[i] = new CircularBuffer<float>(_maxLookaheadSamples);
        }

        _peakBuffer = new CircularBuffer<float>(_maxLookaheadSamples);
        _currentGain = 1f;
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        float thresholdDb = Threshold.CurrentValue;
        float releaseMs = Math.Max(MinReleaseMs, Release.CurrentValue);
        float lookaheadMs = Math.Clamp(Lookahead.CurrentValue, 0f, MaxLookaheadMs);
        float makeupDb = MakeupGain.CurrentValue;

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

        Span<float> thresholds = stackalloc float[Math.Min(input.SampleCount, 1024)];
        Span<float> releases = stackalloc float[Math.Min(input.SampleCount, 1024)];
        Span<float> lookaheads = stackalloc float[Math.Min(input.SampleCount, 1024)];
        Span<float> makeups = stackalloc float[Math.Min(input.SampleCount, 1024)];

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
                float thresholdLin = AudioMath.ConvertDbToLinear(thresholds[i]);
                float makeupLin = AudioMath.ConvertDbToLinear(makeups[i]);
                float releaseMs = Math.Max(MinReleaseMs, releases[i]);
                float lookaheadMs = Math.Clamp(lookaheads[i], 0f, MaxLookaheadMs);
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
        int channelCount = Math.Min(input.ChannelCount, _delayLines!.Length);

        // Stereo-linked peak detection: max of |L|, |R|, ...
        float currentPeak = 0f;
        for (int ch = 0; ch < channelCount; ch++)
        {
            float s = input.GetChannelData(ch)[sampleIndex];
            float abs = MathF.Abs(s);
            if (abs > currentPeak)
                currentPeak = abs;

            _delayLines[ch].Write(s);
        }

        // Push current peak into the peak ring; the windowed-max over the lookahead window
        // tells us the worst incoming peak that has not yet exited the delay line.
        _peakBuffer!.Write(currentPeak);

        float windowPeak = 0f;
        int windowSize = lookaheadSamples + 1;
        for (int j = 0; j < windowSize; j++)
        {
            float v = _peakBuffer.Read(j);
            if (v > windowPeak)
                windowPeak = v;
        }

        // Brick-wall target gain: ratio is effectively infinity.
        float targetGain;
        if (windowPeak > thresholdLin && windowPeak > 0f)
        {
            targetGain = thresholdLin / windowPeak;
        }
        else
        {
            targetGain = 1f;
        }

        // Instant attack, IIR release.
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
