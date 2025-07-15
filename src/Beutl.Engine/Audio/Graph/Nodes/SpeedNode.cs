using Beutl.Animation;
using Beutl.Media;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
            _processor = new SpeedProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
        }

        // If no animation, use static speed processing
        if (!context.AnimationSampler.IsAnimated(Target, SpeedProperty))
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

        // Process with constant speed to get the expected output length
        return _processor!.ProcessBuffer(inputContext, ClampSpeed(_staticSpeed), expectedOutputSampleCount);
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

        // Process with variable speed to get the expected output length
        return _processor!.ProcessBufferWithVariableSpeed(inputContext, speeds, expectedOutputSampleCount);
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
        private readonly SpeedNode _speedNode;
        private readonly WdlResampler _rs;
        private float _currentSpeed = 1.0f;
        private bool _disposed;

        public SpeedProcessor(int sampleRate, int channels, SpeedNode speedNode)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _speedNode = speedNode;

            _rs = new WdlResampler();
            _rs.SetMode(interp: true, filtercnt: 0, sinc: true, sinc_size: 64);
            _rs.SetFilterParms();
            _rs.SetFeedMode(false);
            _rs.SetRates(sampleRate, sampleRate);
        }

        private TimeSpan lastSrcOffset;

        private int Read(int srcOffset, float[] buffer, int offset, int count, AudioProcessContext context)
        {
            var newRange = new TimeRange(TimeSpan.FromSeconds(srcOffset / (float)_sampleRate) + context.TimeRange.Start,
                TimeSpan.FromSeconds(count / (float)_sampleRate));
            if (newRange.End > context.TimeRange.End)
            {
                // Console.WriteLine($"{newRange.End} > {context.TimeRange.End}");
                newRange = newRange.WithDuration(TimeSpan.FromTicks(Math.Max((context.TimeRange.End - newRange.Start).Ticks, 0)));
            }
            var newContext = new AudioProcessContext(
                newRange,
                _sampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);
            var result = _speedNode.Inputs[0].Process(newContext);
            var leftData = result.GetChannelData(0);
            var rightData = result.GetChannelData(1);
            int samplesToRead = Math.Min(buffer.Length / _channels, Math.Min(count, result.SampleCount));
            for (int i = 0; i < samplesToRead; i++)
            {
                buffer[offset + i * _channels] = leftData[i];
                buffer[offset + i * _channels + 1] = rightData[i];
            }

            return result.SampleCount;
        }

        public AudioBuffer ProcessBuffer(AudioProcessContext context, float speed, int expectedOut)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpeedProcessor));

            // 速度変更があればレートだけ更新（Reset は行わない）
            if (Math.Abs(_currentSpeed - speed) > 1e-4f)
            {
                _currentSpeed = speed;
                _rs.SetRates(_sampleRate, _sampleRate / speed);
                // _rs.Reset();  ← ここを呼ぶとフィルタがゼロで埋まり無音になる
            }

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] dst = new float[expectedOut * _channels];

            int framesNeeded = expectedOut; // まだ欲しい出力フレーム数
            int framesDone = 0; // すでに生成したフレーム数
            int srcFramesRead = 0;

            while (framesDone < framesNeeded)
            {
                float[] inBuf;
                int inOff;

                int want = _rs.ResamplePrepare(framesNeeded - framesDone,
                    _channels, out inBuf, out inOff);

                int got = Read(
                    srcFramesRead,
                    inBuf,
                    inOff / _channels,
                    want,
                    context);
                srcFramesRead += got;

                // --- リサンプル ----------------------------------------------------
                int made = _rs.ResampleOut(dst,
                    framesDone * _channels,
                    got, // 供給した入力フレーム数
                    framesNeeded - framesDone, // 欲しい出力数
                    _channels);

                if (made == 0)
                {
                    made = _rs.ResampleOut(dst,
                        framesDone * _channels,
                        0, // 追加入力なし
                        framesNeeded - framesDone,
                        _channels);

                    if (made == 0) break;
                }

                framesDone += made;
            }

            for (int ch = 0; ch < _channels; ch++)
            {
                var chData = output.GetChannelData(ch);
                for (int n = 0; n < framesDone; n++)
                    chData[n] = dst[n * _channels + ch];

                // 生成しきれなかった分は最後の値で埋める
                float tail = framesDone > 0 ? chData[framesDone - 1] : 0f;
                for (int n = framesDone; n < expectedOut; n++)
                    chData[n] = tail;
            }

            return output;
        }

        private const int BLOCK = 256;

        public AudioBuffer ProcessBufferWithVariableSpeed(
            AudioProcessContext context, ReadOnlySpan<float> speedCurve, int expectedOut)
        {
            // TimeRangeは変換前での時間、つまりInputに渡される範囲
            Console.WriteLine($"Start: {context.TimeRange.Start}, End: {context.TimeRange.End}");
            int framesNeeded = expectedOut;
            int framesDone = 0;
            int srcIndexFloor = 0;
            double srcPos = 0.0;

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] dst = new float[expectedOut * _channels];
            // 変換前のサンプル数 (context.TimeRange.Duration.TotalSeconds * _sampleRate) と同じになる
            // ならなかった...

            // Sum: 42291.688
            Console.WriteLine($"Sum: {speedCurve.ToArray().Sum()}");
            var durationSamples = context.TimeRange.Duration.TotalSeconds * _sampleRate;
            // Last: 22050
            Console.WriteLine($"DurationSamples: {durationSamples}");

            // while (srcIndexFloor < last)
            while (framesDone < framesNeeded)
            {
                // 変換後のサンプルレートで計算される
                int framesThis = Math.Min(BLOCK, framesNeeded - framesDone);

                // --- 1. このブロックの速度を全部合計して必要入力フレーム数を計算 ---
                double sumSpeed = 0.0;
                for (int i = 0; i < framesThis; i++)
                    sumSpeed += speedCurve[framesDone + i];

                double needInF    = srcPos + sumSpeed;          // double 精度で次位置
                int    needInInt  = (int)Math.Floor(needInF);   // 整数ぶんを今回読む
                // 変換前
                int    wantFrames = needInInt - srcIndexFloor;  // 追加で必要なフレーム数

                double vAvg = sumSpeed / framesThis;
                Console.WriteLine(vAvg);
                double outRate = _sampleRate / vAvg; // ratio = vAvg
                _rs.SetRates(_sampleRate, outRate);

                float[] inBuf;
                int inOff;
                int willNeed = _rs.ResamplePrepare(framesThis, _channels, out inBuf, out inOff);

                if (wantFrames > willNeed) wantFrames = willNeed;

                // 本当に読む
                int got = Read(srcIndexFloor, inBuf, inOff, wantFrames, context);
                // srcIndexFloor += got;
                if (got < wantFrames)
                {
                    Array.Clear(inBuf, inOff + got * _channels,
                        (wantFrames - got) * _channels);
                }

                int made = _rs.ResampleOut(dst,
                    framesDone * _channels,
                    wantFrames, // 供給した入力フレーム数
                    framesThis, // 欲しい出力数
                    _channels);

                framesDone += made;
                srcIndexFloor += wantFrames;
                srcPos = needInF;
                // このメソッド呼び出しでの最後の出力がcontext.TimeRange.Endと同じになる必要があるが、この出力の方が大きくなってしまっている
                // フレームが過剰に供給されていることが考えられたが
                Console.WriteLine(TimeSpan.FromSeconds(srcIndexFloor / (double)_sampleRate)+ context.TimeRange.Start);

                // if (made == 0)
                // {
                //     made = _rs.ResampleOut(dst,
                //         framesDone * _channels,
                //         0, // 追加入力なし
                //         framesThis,
                //         _channels);
                //
                //     if (made == 0) break;
                // }
            }

            // srcPos: 42291.687
            // srcIndexFloor: 42291
            Console.WriteLine($"srcPos: {srcPos:F}");
            Console.WriteLine($"srcIndexFloor: {srcIndexFloor}");

            for (int ch = 0; ch < _channels; ch++)
            {
                var chData = output.GetChannelData(ch);
                for (int n = 0; n < framesDone; n++)
                    chData[n] = dst[n * _channels + ch];

                // 生成しきれなかった分は最後の値で埋める
                float tail = framesDone > 0 ? chData[framesDone - 1] : 0f;
                for (int n = framesDone; n < expectedOut; n++)
                    chData[n] = tail;
            }

            return output;
        }

        private static float Average(ReadOnlySpan<float> s)
            => s.IsEmpty ? 1f : (float)(s.ToArray().Average());

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
                _disposed = true;
            }
        }
    }
}
