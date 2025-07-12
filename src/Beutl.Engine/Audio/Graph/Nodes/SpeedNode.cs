using Beutl.Animation;
using Beutl.Media;
using NAudio.Dsp;
using NAudio.Wave;

namespace Beutl.Audio.Graph.Nodes;

public sealed class SpeedNode : AudioNode
{
    private float _staticSpeed = 1.0f;

    // Processor for audio speed processing
    private SpeedProcessor? _processor;
    private int _lastSampleRate;

    public IAnimatable? Target { get; set; }

    public CoreProperty<float>? SpeedProperty { get; set; }

    public float StaticSpeed
    {
        get => _staticSpeed;
        set
        {
            var clampedValue = ClampSpeed(value);
            _staticSpeed = clampedValue;
        }
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Variable speed node requires exactly one input.");

        // Calculate the expected output sample count based on the context's time range
        var expectedOutputSampleCount = context.GetSampleCount();

        // Initialize processor if needed
        if (_processor == null || _lastSampleRate != context.SampleRate)
        {
            _processor?.Dispose();
            _processor = new SpeedProcessor(context.SampleRate);
            _lastSampleRate = context.SampleRate;
        }

        // If no animation, use static speed processing
        if (Target == null || SpeedProperty == null)
        {
            return ProcessStaticSpeed(context, expectedOutputSampleCount);
        }

        return ProcessAnimatedSpeed(context, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessStaticSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        // If speed is 1.0, use normal processing
        if (System.Math.Abs(_staticSpeed - 1.0f) < float.Epsilon)
        {
            return Inputs[0].Process(context);
        }

        // Calculate source time range from output time range
        var sourceTimeRange = CalculateSourceTimeRange(context.TimeRange, _staticSpeed);

        // Create input context with calculated source time range
        var inputContext = new AudioProcessContext(
            sourceTimeRange,
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        // Process input from calculated source range
        var input = Inputs[0].Process(inputContext);

        // Process with constant speed to get the expected output length
        return _processor!.ProcessBuffer(input, ClampSpeed(_staticSpeed), expectedOutputSampleCount);
    }

    private AudioBuffer ProcessAnimatedSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        // Sample speed values for the entire output buffer
        Span<float> speeds = stackalloc float[expectedOutputSampleCount];

        context.AnimationSampler.SampleBuffer(
            Target!,
            SpeedProperty!,
            context.TimeRange,
            context.SampleRate,
            speeds);

        // Clamp all speed values to safe ranges
        for (int i = 0; i < speeds.Length; i++)
        {
            speeds[i] = ClampSpeed(speeds[i] / 100f);
        }

        // Calculate source time range from output time range using keyframe animation
        var sourceTimeRange = CalculateSourceTimeRangeAnimated(context.TimeRange);

        // Create input context with calculated source time range
        var inputContext = new AudioProcessContext(
            sourceTimeRange,
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        // Process input from calculated source range
        var input = Inputs[0].Process(inputContext);

        // Process with variable speed to get the expected output length
        return _processor!.ProcessBufferWithVariableSpeed(input, speeds, expectedOutputSampleCount);
    }

    /// <summary>
    /// Calculate source time range from output time range using static speed.
    /// Similar to SourceVideo.CalculateVideoTime() approach.
    /// </summary>
    private TimeRange CalculateSourceTimeRange(TimeRange outputTimeRange, float speed)
    {
        // For static speed: sourceTime = outputTime * speed
        var sourceStart = CalculateSourceTime(outputTimeRange.Start, speed);
        var sourceDuration = TimeSpan.FromTicks((long)(outputTimeRange.Duration.Ticks * speed));

        return new TimeRange(sourceStart, sourceDuration);
    }

    /// <summary>
    /// Calculate source time range from output time range using animated speed curve.
    /// Uses keyframe-based calculation similar to SourceVideo's approach.
    /// </summary>
    private TimeRange CalculateSourceTimeRangeAnimated(TimeRange outputTimeRange)
    {
        // For animated speed, calculate both start and end times using keyframe integration
        var sourceStart = CalculateSourceTime(outputTimeRange.Start);
        var sourceEnd = CalculateSourceTime(outputTimeRange.End);

        // Calculate duration from the difference
        var sourceDuration = sourceEnd - sourceStart;

        // Ensure duration is positive
        if (sourceDuration < TimeSpan.Zero)
            sourceDuration = TimeSpan.FromTicks(-sourceDuration.Ticks);

        return new TimeRange(sourceStart, sourceDuration);
    }

    /// <summary>
    /// Calculate source time from output time using speed integration.
    /// This maps any output time to corresponding source time.
    /// Similar to SourceVideo.CalculateVideoTime() approach.
    /// </summary>
    private TimeSpan CalculateSourceTime(TimeSpan outputTime, float? staticSpeed = null)
    {
        // If we have a static speed, use simple calculation
        if (staticSpeed.HasValue)
        {
            return TimeSpan.FromTicks(SafeMultiplyTicks(outputTime.Ticks, ClampSpeed(staticSpeed.Value)));
        }

        // For animated speed, we need to check if we have animation
        if (Target == null || SpeedProperty == null)
        {
            return TimeSpan.FromTicks(SafeMultiplyTicks(outputTime.Ticks, ClampSpeed(_staticSpeed)));
        }

        // Get the speed animation if it exists
        var speedAnimation = GetSpeedAnimation();
        if (speedAnimation == null)
        {
            return TimeSpan.FromTicks((long)(outputTime.Ticks * _staticSpeed));
        }

        return CalculateVideoTimeFromKeyframes(outputTime, speedAnimation);
    }

    /// <summary>
    /// Get the speed animation from the target object.
    /// </summary>
    private IAnimation? GetSpeedAnimation()
    {
        if (Target?.Animations == null || SpeedProperty == null)
            return null;

        return Target.Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id);
    }

    /// <summary>
    /// Calculate source time using keyframe animation, similar to SourceVideo.CalculateVideoTime().
    /// </summary>
    private TimeSpan CalculateVideoTimeFromKeyframes(TimeSpan outputTime, IAnimation animation)
    {
        if (animation is not KeyFrameAnimation<float> keyFrameAnimation)
            return TimeSpan.FromTicks(SafeMultiplyTicks(outputTime.Ticks, ClampSpeed(_staticSpeed)));

        // If no keyframes, use static speed
        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks(SafeMultiplyTicks(outputTime.Ticks, ClampSpeed(_staticSpeed)));
        }

        int kfi = keyFrameAnimation.KeyFrames.IndexAt(outputTime);
        if (kfi < 0 || kfi >= keyFrameAnimation.KeyFrames.Count)
        {
            // Fallback to static speed if index is invalid
            return TimeSpan.FromTicks(SafeMultiplyTicks(outputTime.Ticks, ClampSpeed(_staticSpeed)));
        }

        var kf = (KeyFrame<float>)keyFrameAnimation.KeyFrames[kfi];
        float clampedSpeed = ClampSpeed(kf.Value / 100f);

        // If there's a previous keyframe, recursively calculate base time
        if (kfi > 0 &&
            keyFrameAnimation.KeyFrames[kfi - 1] is KeyFrame<float> prevKf)
        {
            var baseSourceTime = CalculateVideoTimeFromKeyframes(prevKf.KeyTime, animation);
            var deltaTicks = (outputTime - prevKf.KeyTime).Ticks;
            // Apply current keyframe's speed to the delta (with overflow protection)
            long sourceTicks = SafeMultiplyTicks(deltaTicks, clampedSpeed);
            return baseSourceTime + TimeSpan.FromTicks(sourceTicks);
        }

        // First keyframe case (with overflow protection)
        return TimeSpan.FromTicks(SafeMultiplyTicks(outputTime.Ticks, clampedSpeed));
    }

    /// <summary>
    /// Clamp speed value to prevent extreme values that could cause issues.
    /// </summary>
    private static float ClampSpeed(float speed)
    {
        // Clamp speed to reasonable bounds (0.01x to 100x)
        // Prevents division by zero and extreme resampling rates
        return System.Math.Max(0.01f, System.Math.Min(100.0f, System.Math.Abs(speed)));
    }

    /// <summary>
    /// Safely multiply ticks by speed factor with overflow protection.
    /// </summary>
    private static long SafeMultiplyTicks(long ticks, float speed)
    {
        try
        {
            // Use checked arithmetic to detect overflow
            checked
            {
                return (long)(ticks * speed);
            }
        }
        catch (OverflowException)
        {
            // Return max/min value based on sign to prevent overflow
            return speed >= 0 ? long.MaxValue : long.MinValue;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _processor?.Dispose();
            _processor = null;
        }

        base.Dispose(disposing);
    }

    private sealed class SpeedProcessor : IDisposable
    {
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly WdlResampler _rs;
        private readonly StreamingSampleProvider _stream;
        private float _currentSpeed = 1.0f;
        private bool _disposed;

        public SpeedProcessor(int sampleRate, int channels = 2)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _stream = new StreamingSampleProvider(sampleRate, channels);

            _rs = new WdlResampler();
            _rs.SetMode(interp: true, filtercnt: 0, sinc: true, sinc_size: 64);
            _rs.SetFilterParms();
            _rs.SetFeedMode(false);
            _rs.SetRates(sampleRate, sampleRate);
        }

        public AudioBuffer ProcessBuffer(AudioBuffer input,
            float speed, int expectedOut)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpeedProcessor));

            _stream.AddSamples(input);

            if (Math.Abs(_currentSpeed - speed) > 1e-4f)
            {
                _currentSpeed = speed;
                _rs.SetRates(_sampleRate, _sampleRate / speed);
            }

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            int framesNeeded = expectedOut;

            float[] dst = new float[framesNeeded * _channels];
            int framesDone = 0;

            while (framesDone < framesNeeded)
            {
                float[] inBuf;
                int inOff;
                int want = _rs.ResamplePrepare(framesNeeded - framesDone,
                    _channels, out inBuf, out inOff);

                // キューから必要分だけ取り出してinBufへ
                int got = _stream.Read(inBuf, inOff * _channels,
                    want * _channels) / _channels;

                int made = _rs.ResampleOut(dst,
                    framesDone * _channels,
                    got, framesNeeded - framesDone,
                    _channels);

                framesDone += made;
                if (got == 0) break;
            }

            // ---- de-interleave ----
            for (int ch = 0; ch < _channels; ch++)
            {
                var chData = output.GetChannelData(ch);
                for (int n = 0; n < framesDone; n++)
                    chData[n] = dst[n * _channels + ch];

                // 取り切れなかったら無音パディング
                for (int n = framesDone; n < expectedOut; n++)
                    chData[n] = 0f;
            }

            return output;
        }

        public AudioBuffer ProcessBufferWithVariableSpeed(
            AudioBuffer input, ReadOnlySpan<float> speedCurve,
            int expectedOut)
        {
            const int BLOCK = 2048;
            int outDone = 0, inDone = 0;
            var outBuf = new AudioBuffer(_sampleRate, _channels, expectedOut);

            while (outDone < expectedOut && inDone < input.SampleCount)
            {
                int wantOut = Math.Min(BLOCK, expectedOut - outDone);
                float v = Average(speedCurve.Slice(outDone, wantOut));
                int needIn = (int)Math.Round(wantOut * v);

                var inChunk = ExtractChunk(input, inDone, needIn);
                var outChunk = ProcessBuffer(inChunk, v, wantOut);

                MergeChunk(outBuf, outChunk, outDone);

                outDone += wantOut;
                inDone += needIn;
            }

            return outBuf;
        }

        private static float Average(ReadOnlySpan<float> s)
            => s.IsEmpty ? 1f : (float)(s.ToArray().Average());

        private AudioBuffer ExtractChunk(AudioBuffer input, int startSample, int chunkSize)
        {
            var chunk = new AudioBuffer(input.SampleRate, input.ChannelCount, chunkSize);

            for (int ch = 0; ch < input.ChannelCount; ch++)
            {
                var inputChannel = input.GetChannelData(ch);
                var chunkChannel = chunk.GetChannelData(ch);

                for (int i = 0; i < chunkSize; i++)
                {
                    if (startSample + i < input.SampleCount)
                    {
                        chunkChannel[i] = inputChannel[startSample + i];
                    }
                    else
                    {
                        chunkChannel[i] = 0.0f; // Pad with silence
                    }
                }
            }

            return chunk;
        }

        private void MergeChunk(AudioBuffer output, AudioBuffer chunk, int startSample)
        {
            for (int ch = 0; ch < output.ChannelCount; ch++)
            {
                var outputChannel = output.GetChannelData(ch);
                var chunkChannel = chunk.GetChannelData(ch);

                int samplesToCopy = System.Math.Min(chunk.SampleCount, output.SampleCount - startSample);

                for (int i = 0; i < samplesToCopy; i++)
                {
                    outputChannel[startSample + i] = chunkChannel[i];
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stream.Dispose();
                _disposed = true;
            }
        }
    }

    private sealed class StreamingSampleProvider : ISampleProvider, IDisposable
    {
        private readonly WaveFormat _waveFormat;
        private readonly Queue<float> _sampleQueue = new();
        private bool _disposed;

        public StreamingSampleProvider(int sampleRate, int channelCount)
        {
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
        }

        public WaveFormat WaveFormat => _waveFormat;

        public void AddSamples(AudioBuffer buffer)
        {
            if (_disposed) return;

            // Interleave samples and add to queue
            for (int i = 0; i < buffer.SampleCount; i++)
            {
                for (int ch = 0; ch < buffer.ChannelCount; ch++)
                {
                    var channelData = buffer.GetChannelData(ch);
                    _sampleQueue.Enqueue(channelData[i]);
                }
            }
        }

        public void Clear()
        {
            if (!_disposed)
            {
                _sampleQueue.Clear();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_disposed)
                return 0;

            int samplesRead = 0;
            int maxSamples = System.Math.Min(count, _sampleQueue.Count);

            for (int i = 0; i < maxSamples; i++)
            {
                if (_sampleQueue.Count > 0)
                {
                    buffer[offset + i] = _sampleQueue.Dequeue();
                    samplesRead++;
                }
                else
                {
                    // Pad with silence if no more samples
                    buffer[offset + i] = 0.0f;
                    samplesRead++;
                }
            }

            return samplesRead;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _sampleQueue.Clear();
                _disposed = true;
            }
        }
    }
}
