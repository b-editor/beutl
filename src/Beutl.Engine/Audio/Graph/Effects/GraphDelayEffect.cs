using System;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph.Effects;

public sealed class GraphDelayEffect : Animatable, IAudioEffect
{
    public static readonly CoreProperty<float> DelayTimeProperty;
    public static readonly CoreProperty<float> FeedbackProperty;
    public static readonly CoreProperty<float> MixProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;

    private float _delayTime = 200f; // milliseconds
    private float _feedback = 50f;   // percentage
    private float _mix = 30f;        // percentage
    private bool _isEnabled = true;

    static GraphDelayEffect()
    {
        DelayTimeProperty = ConfigureProperty<float, GraphDelayEffect>(nameof(DelayTime))
            .Accessor(o => o.DelayTime, (o, v) => o.DelayTime = v)
            .DefaultValue(200f)
            .Register();

        FeedbackProperty = ConfigureProperty<float, GraphDelayEffect>(nameof(Feedback))
            .Accessor(o => o.Feedback, (o, v) => o.Feedback = v)
            .DefaultValue(50f)
            .Register();

        MixProperty = ConfigureProperty<float, GraphDelayEffect>(nameof(Mix))
            .Accessor(o => o.Mix, (o, v) => o.Mix = v)
            .DefaultValue(30f)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, GraphDelayEffect>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();
    }

    public float DelayTime
    {
        get => _delayTime;
        set => SetAndRaise(DelayTimeProperty, ref _delayTime, System.Math.Clamp(value, 0f, 5000f));
    }

    public float Feedback
    {
        get => _feedback;
        set => SetAndRaise(FeedbackProperty, ref _feedback, System.Math.Clamp(value, 0f, 100f));
    }

    public float Mix
    {
        get => _mix;
        set => SetAndRaise(MixProperty, ref _mix, System.Math.Clamp(value, 0f, 100f));
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public IAudioEffectProcessor CreateProcessor()
    {
        return new GraphDelayProcessor(this);
    }

    private sealed class GraphDelayProcessor : IAudioEffectProcessor
    {
        private readonly GraphDelayEffect _effect;
        private CircularBuffer<float>[]? _delayLines;
        private int _maxDelaySamples;
        private bool _disposed;

        public GraphDelayProcessor(GraphDelayEffect effect)
        {
            _effect = effect ?? throw new ArgumentNullException(nameof(effect));
        }

        public void Prepare(TimeRange range, int sampleRate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Calculate maximum delay needed (5 seconds max + some headroom)
            _maxDelaySamples = (int)(5.5f * sampleRate);
            
            // Dispose existing delay lines if they exist
            if (_delayLines != null)
            {
                foreach (var line in _delayLines)
                {
                    line?.Dispose();
                }
            }

            // Create new delay lines for each channel (assuming stereo for now)
            _delayLines = new CircularBuffer<float>[2];
            for (int i = 0; i < _delayLines.Length; i++)
            {
                _delayLines[i] = new CircularBuffer<float>(_maxDelaySamples);
            }
        }

        public void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_delayLines == null)
            {
                Prepare(context.TimeRange, context.SampleRate);
            }

            if (input.ChannelCount != output.ChannelCount || input.SampleCount != output.SampleCount)
                throw new ArgumentException("Input and output buffers must have the same format.");

            if (!_effect.IsEnabled)
            {
                // Pass through
                input.CopyTo(output);
                return;
            }

            // Sample animation values
            Span<float> delayTimes = stackalloc float[System.Math.Min(input.SampleCount, 1024)];
            Span<float> feedbacks = stackalloc float[System.Math.Min(input.SampleCount, 1024)];
            Span<float> mixes = stackalloc float[System.Math.Min(input.SampleCount, 1024)];

            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = System.Math.Min(delayTimes.Length, input.SampleCount - processed);
                
                var chunkStart = context.GetTimeForSample(processed);
                var chunkEnd = context.GetTimeForSample(processed + chunkSize);
                var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

                // Sample animation values for this chunk
                context.AnimationSampler.SampleBuffer(_effect, GraphDelayEffect.DelayTimeProperty, chunkRange, chunkSize, delayTimes.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect, GraphDelayEffect.FeedbackProperty, chunkRange, chunkSize, feedbacks.Slice(0, chunkSize));
                context.AnimationSampler.SampleBuffer(_effect, GraphDelayEffect.MixProperty, chunkRange, chunkSize, mixes.Slice(0, chunkSize));

                // Process each channel
                for (int ch = 0; ch < System.Math.Min(input.ChannelCount, _delayLines!.Length); ch++)
                {
                    var inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    var outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    var delayLine = _delayLines[ch];

                    for (int i = 0; i < chunkSize; i++)
                    {
                        // Convert delay time from milliseconds to samples
                        var delaySamples = (int)(delayTimes[i] * context.SampleRate / 1000f);
                        delaySamples = System.Math.Clamp(delaySamples, 0, _maxDelaySamples - 1);

                        // Read delayed sample
                        var delayedSample = delayLine.Read(delaySamples);
                        
                        // Calculate feedback and mix
                        var feedbackAmount = feedbacks[i] / 100f;
                        var mixAmount = mixes[i] / 100f;
                        
                        // Apply feedback to delay line
                        var feedbackSample = inData[i] + (delayedSample * feedbackAmount);
                        delayLine.Write(feedbackSample);
                        
                        // Mix dry and wet signals
                        outData[i] = inData[i] * (1f - mixAmount) + delayedSample * mixAmount;
                    }
                }

                // Handle additional channels by copying input directly
                for (int ch = _delayLines!.Length; ch < input.ChannelCount; ch++)
                {
                    var inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    var outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    inData.CopyTo(outData);
                }

                processed += chunkSize;
            }
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_delayLines != null)
            {
                foreach (var line in _delayLines)
                {
                    line?.Clear();
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_delayLines != null)
                {
                    foreach (var line in _delayLines)
                    {
                        line?.Dispose();
                    }
                    _delayLines = null;
                }
                _disposed = true;
            }
        }
    }
}