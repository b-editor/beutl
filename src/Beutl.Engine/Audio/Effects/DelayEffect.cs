using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Effects;

public sealed partial class DelayEffect : AudioEffect
{
    private const float MaxDelayTime = 5000f; // 5 seconds in milliseconds

    public DelayEffect()
    {
        ScanProperties<DelayEffect>();
    }

    [Range(0, MaxDelayTime)]
    [Display(Name = "Delay Time (ms)")]
    public IProperty<float> DelayTime { get; } = Property.Create(200f);

    [Range(0, 100)]
    [Display(Name = "Feedback (%)")]
    public IProperty<float> Feedback { get; } = Property.Create(50f);

    [Range(0, 100)]
    [Display(Name = "Dry Mix (%)")]
    public IProperty<float> DryMix { get; } = Property.Create(60f);

    [Range(0, 100)]
    [Display(Name = "Wet Mix (%)")]
    public IProperty<float> WetMix { get; } = Property.Create(40f);

    public override IAudioEffectProcessor CreateProcessor()
    {
        return new DelayProcessor(this);
    }

    private sealed class DelayProcessor : IAudioEffectProcessor
    {
        private readonly DelayEffect _effect;
        private CircularBuffer<float>[]? _delayLines;
        private readonly int _maxDelaySamples;

        public DelayProcessor(DelayEffect effect)
        {
            _effect = effect;
            // Assume max sample rate of 192kHz for buffer allocation
            _maxDelaySamples = (int)(MaxDelayTime / 1000f * 192000);
        }

        public void Prepare(TimeRange range, int sampleRate)
        {
            // Initialize delay lines if needed
            if (_delayLines == null)
            {
                _delayLines = new CircularBuffer<float>[2]; // Stereo
                for (int i = 0; i < _delayLines.Length; i++)
                {
                    _delayLines[i] = new CircularBuffer<float>(_maxDelaySamples);
                }
            }
        }

        public void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context)
        {
            if (_delayLines == null)
            {
                Prepare(context.TimeRange, context.SampleRate);
            }

            // Get animation values for this buffer
            Span<float> delayTimes = stackalloc float[System.Math.Min(input.SampleCount, 1024)];
            Span<float> feedbacks = stackalloc float[System.Math.Min(input.SampleCount, 1024)];
            Span<float> dryMixes = stackalloc float[System.Math.Min(input.SampleCount, 1024)];
            Span<float> wetMixes = stackalloc float[System.Math.Min(input.SampleCount, 1024)];

            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = System.Math.Min(delayTimes.Length, input.SampleCount - processed);

                var chunkStart = context.GetTimeForSample(processed);
                var chunkEnd = context.GetTimeForSample(processed + chunkSize);
                var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

                // Sample animation values for this chunk
                context.AnimationSampler.SampleBuffer(_effect.DelayTime, chunkRange, context.SampleRate, delayTimes.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect.Feedback, chunkRange, context.SampleRate, feedbacks.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect.DryMix, chunkRange, context.SampleRate, dryMixes.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect.WetMix, chunkRange, context.SampleRate, wetMixes.Slice(0, chunkSize));

                // Process each channel
                for (int ch = 0; ch < System.Math.Min(input.ChannelCount, _delayLines!.Length); ch++)
                {
                    var inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    var outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    var delayLine = _delayLines[ch];

                    for (int i = 0; i < chunkSize; i++)
                    {
                        // Convert delay time from ms to samples
                        int delaySamples = (int)(delayTimes[i] / 1000f * context.SampleRate);
                        delaySamples = System.Math.Clamp(delaySamples, 0, _maxDelaySamples - 1);

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

        public void Dispose()
        {
            if (_delayLines != null)
            {
                foreach (var line in _delayLines)
                {
                    line.Dispose();
                }
                _delayLines = null;
            }
        }
    }
}
