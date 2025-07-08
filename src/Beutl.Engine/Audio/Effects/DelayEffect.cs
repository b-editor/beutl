using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.Audio.Effects;

public sealed class DelayEffect : AudioEffect
{
    public static readonly CoreProperty<float> DelayTimeProperty;
    public static readonly CoreProperty<float> FeedbackProperty;
    public static readonly CoreProperty<float> DryMixProperty;
    public static readonly CoreProperty<float> WetMixProperty;

    private const float MaxDelayTime = 5000f; // 5 seconds in milliseconds
    private float _delayTime = 200f; // milliseconds
    private float _feedback = 50f;   // percentage
    private float _dryMix = 60f;    // percentage
    private float _wetMix = 40f;    // percentage

    static DelayEffect()
    {
        DelayTimeProperty = ConfigureProperty<float, DelayEffect>(nameof(DelayTime))
            .Accessor(o => o.DelayTime, (o, v) => o.DelayTime = v)
            .DefaultValue(200f)
            .Register();

        FeedbackProperty = ConfigureProperty<float, DelayEffect>(nameof(Feedback))
            .Accessor(o => o.Feedback, (o, v) => o.Feedback = v)
            .DefaultValue(50f)
            .Register();

        DryMixProperty = ConfigureProperty<float, DelayEffect>(nameof(DryMix))
            .Accessor(o => o.DryMix, (o, v) => o.DryMix = v)
            .DefaultValue(60f)
            .Register();

        WetMixProperty = ConfigureProperty<float, DelayEffect>(nameof(WetMix))
            .Accessor(o => o.WetMix, (o, v) => o.WetMix = v)
            .DefaultValue(40f)
            .Register();

        AffectsRender<DelayEffect>(DelayTimeProperty, FeedbackProperty, DryMixProperty, WetMixProperty);
    }

    [Range(0, MaxDelayTime)]
    [Display(Name = "Delay Time (ms)")]
    public float DelayTime
    {
        get => _delayTime;
        set => SetAndRaise(DelayTimeProperty, ref _delayTime, System.Math.Clamp(value, 0f, MaxDelayTime));
    }

    [Range(0, 100)]
    [Display(Name = "Feedback (%)")]
    public float Feedback
    {
        get => _feedback;
        set => SetAndRaise(FeedbackProperty, ref _feedback, System.Math.Clamp(value, 0f, 100f));
    }

    [Range(0, 100)]
    [Display(Name = "Dry Mix (%)")]
    public float DryMix
    {
        get => _dryMix;
        set => SetAndRaise(DryMixProperty, ref _dryMix, System.Math.Clamp(value, 0f, 100f));
    }

    [Range(0, 100)]
    [Display(Name = "Wet Mix (%)")]
    public float WetMix
    {
        get => _wetMix;
        set => SetAndRaise(WetMixProperty, ref _wetMix, System.Math.Clamp(value, 0f, 100f));
    }

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
                context.AnimationSampler.SampleBuffer(_effect, DelayEffect.DelayTimeProperty, chunkRange, chunkSize, delayTimes.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect, DelayEffect.FeedbackProperty, chunkRange, chunkSize, feedbacks.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect, DelayEffect.DryMixProperty, chunkRange, chunkSize, dryMixes.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect, DelayEffect.WetMixProperty, chunkRange, chunkSize, wetMixes.Slice(0, chunkSize));

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
