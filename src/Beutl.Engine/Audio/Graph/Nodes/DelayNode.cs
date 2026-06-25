using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Media;
using static Beutl.Audio.Effects.DelayParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class DelayNode : AudioNode
{
    private CircularBuffer<float>[]? _delayLines;
    private int _maxDelaySamples;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    public required IProperty<float> DelayTime { get; init; }

    public required IProperty<float> Feedback { get; init; }

    public required IProperty<float> DryMix { get; init; }

    public required IProperty<float> WetMix { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Delay node requires exactly one input.");

        return ProcessTail(Inputs[0].Process(context), context, draining: false);
    }

    // Deliberately keeps the base GetLatencySamples of 0: an echo/delay is an intentional, audible
    // delay (render-tail-time territory), not processing latency that PDC compensates — reporting it
    // would shift the dry signal earlier and double-count the effect. A consequence is that a wet-only
    // delay placed after a lookahead limiter is not granted extra flush budget, so its delayed tail can
    // still be clipped at the clip boundary; recovering that is a separate render-tail-time concern.

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining)
    {
        // Every path emits a fresh buffer (no pass-through), so dispose the consumed input.
        using var owned = input;

        // Reinitialize on a sample-rate or channel-count change; otherwise a channel-count increase
        // would leave the extra channels unprocessed (silent).
        if (_delayLines == null
            || _lastSampleRate != context.SampleRate
            || _delayLines.Length != input.ChannelCount)
        {
            InitializeDelayLines(context.SampleRate, input.ChannelCount);
            _lastSampleRate = context.SampleRate;
        }

        // Reset on the first call or any seek (a chunk not starting where the previous one ended).
        // The node is cached across Compose() calls, so stale delay-line content would otherwise
        // bleed in. Matches CompressorNode/EqualizerNode.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            Reset();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        // Check Animation (actual keyframe), not IsAnimatable (always true for animatable properties).
        bool hasAnimation = DelayTime.Animation != null ||
                            Feedback.Animation != null ||
                            DryMix.Animation != null ||
                            WetMix.Animation != null;

        if (!hasAnimation)
        {
            return ProcessStatic(input, context);
        }

        return ProcessAnimated(input, context);
    }

    private void InitializeDelayLines(int sampleRate, int channelCount)
    {
        // Dispose existing delay lines
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Dispose();
            }
        }

        _maxDelaySamples = (int)(DelayTimeMax / 1000f * sampleRate);
        _delayLines = new CircularBuffer<float>[channelCount];
        for (int i = 0; i < _delayLines.Length; i++)
        {
            _delayLines[i] = new CircularBuffer<float>(_maxDelaySamples);
        }
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        float delayTime = DelayTime.CurrentValue;
        float feedback = Feedback.CurrentValue / 100f;
        float dryMix = DryMix.CurrentValue / 100f;
        float wetMix = WetMix.CurrentValue / 100f;

        int delaySamples = (int)(delayTime / 1000f * context.SampleRate);
        delaySamples = System.Math.Clamp(delaySamples, 0, _maxDelaySamples - 1);

        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            for (int ch = 0; ch < System.Math.Min(input.ChannelCount, _delayLines!.Length); ch++)
            {
                var inData = input.GetChannelData(ch);
                var outData = output.GetChannelData(ch);
                var delayLine = _delayLines[ch];

                for (int i = 0; i < input.SampleCount; i++)
                {
                    float inputSample = inData[i];
                    float delayedSample = delayLine.Read(delaySamples);

                    // Write to delay line with feedback
                    delayLine.Write(inputSample + delayedSample * feedback);

                    // Mix dry and wet signals
                    outData[i] = inputSample * dryMix + delayedSample * wetMix;
                }
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

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            // Sample animation values for each sample
            Span<float> delayTimes = stackalloc float[Math.Min(input.SampleCount, 1024)];
            Span<float> feedbacks = stackalloc float[Math.Min(input.SampleCount, 1024)];
            Span<float> dryMixes = stackalloc float[Math.Min(input.SampleCount, 1024)];
            Span<float> wetMixes = stackalloc float[Math.Min(input.SampleCount, 1024)];

            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = Math.Min(delayTimes.Length, input.SampleCount - processed);

                var chunkStart = context.GetTimeForSample(processed);
                var chunkEnd = context.GetTimeForSample(processed + chunkSize);
                var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

                // Sample animation values for this chunk
                context.AnimationSampler.SampleBuffer(DelayTime, chunkRange, context.SampleRate, delayTimes[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Feedback, chunkRange, context.SampleRate, feedbacks[..chunkSize]);
                context.AnimationSampler.SampleBuffer(DryMix, chunkRange, context.SampleRate, dryMixes[..chunkSize]);
                context.AnimationSampler.SampleBuffer(WetMix, chunkRange, context.SampleRate, wetMixes[..chunkSize]);

                // Process each channel
                for (int ch = 0; ch < Math.Min(input.ChannelCount, _delayLines!.Length); ch++)
                {
                    var inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    var outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    var delayLine = _delayLines[ch];

                    for (int i = 0; i < chunkSize; i++)
                    {
                        // Convert delay time from ms to samples
                        int delaySamples = (int)(delayTimes[i] / 1000f * context.SampleRate);
                        delaySamples = Math.Clamp(delaySamples, 0, _maxDelaySamples - 1);

                        float inputSample = inData[i];
                        float delayedSample = delayLine.Read(delaySamples);

                        float feedback = feedbacks[i] / 100f;
                        float dryMix = dryMixes[i] / 100f;
                        float wetMix = wetMixes[i] / 100f;

                        // Write to delay line with feedback
                        delayLine.Write(inputSample + delayedSample * feedback);

                        // Mix dry and wet signals
                        outData[i] = inputSample * dryMix + delayedSample * wetMix;
                    }
                }

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

    public void Reset()
    {
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Clear();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Dispose();
            }

            _delayLines = null;
        }

        base.Dispose(disposing);
    }
}
