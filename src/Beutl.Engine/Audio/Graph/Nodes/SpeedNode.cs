using Beutl.Animation;
using Beutl.Media;
using NAudio.Dsp;

namespace Beutl.Audio.Graph.Nodes;

public sealed class SpeedNode : AudioNode
{
    // Processor for audio speed processing
    private SpeedProcessor? _processor;
    private int _lastSampleRate;

    // 秒数 -> サンプルオフセット
    private Dictionary<int, double> _cache = new();
    private IAnimatable? _target;
    private CoreProperty<float>? _speedProperty;
    private KeyFrameAnimation<float>? _animation;

    public IAnimatable? Target
    {
        get => _target;
        set
        {
            _target = value;
            if (!ReferenceEquals(_target, value))
            {
                InvalidateCache();
            }
        }
    }

    public CoreProperty<float>? SpeedProperty
    {
        get => _speedProperty;
        set
        {
            _speedProperty = value;
            if (!ReferenceEquals(_speedProperty, value))
            {
                InvalidateCache();
            }
        }
    }

    public float StaticSpeed { get; set; } = 1.0f;

    private void InvalidateCache()
    {
        _cache.Clear();
        _processor = null;
    }

    private void OnAnimationInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        InvalidateCache();
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Variable speed node requires exactly one input.");

        // Calculate the expected output sample count based on the context's time range
        var expectedOutputSampleCount = context.GetSampleCount();

        var animation = GetSpeedAnimation();
        if (!ReferenceEquals(animation, _animation))
        {
            InvalidateCache();
            if (_animation != null)
                _animation.Invalidated -= OnAnimationInvalidated;

            if (animation != null)
                animation.Invalidated += OnAnimationInvalidated;

            _animation = animation;
        }

        // Initialize processor if needed
        if (_processor == null || _lastSampleRate != context.SampleRate)
        {
            _processor = new SpeedProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
        }

        if (animation == null)
        {
            return ProcessStaticSpeed(context, expectedOutputSampleCount);
        }

        return ProcessAnimatedSpeed(context, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessStaticSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        // If speed is 1.0, use normal processing
        if (Math.Abs(StaticSpeed - 1.0f) < float.Epsilon)
        {
            return Inputs[0].Process(context);
        }

        // Calculate source time range from output time range
        var sourceTimeRange = CalculateSourceTimeRange(context.TimeRange, StaticSpeed);

        // Create input context with calculated source time range
        var inputContext = new AudioProcessContext(
            sourceTimeRange,
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        // Process with constant speed to get the expected output length
        return _processor!.ProcessBuffer(inputContext, StaticSpeed, expectedOutputSampleCount);
    }

    private (int Key, double Value) TryGetCache(int sec)
    {
        // キャッシュヒット確認
        do
        {
            if (_cache.TryGetValue(sec--, out double result))
            {
                return (sec + 1, result);
            }
        } while (sec >= 0);

        return (-1, 0);
    }

    private AudioBuffer ProcessAnimatedSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        var origStartSec = (int)context.TimeRange.Start.TotalSeconds;

        (int startSec, double startConvTime) = TryGetCache(origStartSec);

        // 見つからなかった場合、-1が帰ってくるので0にする
        if (startSec < 0) startSec++;
        // 元々の開始秒数と見つかった秒数が違う場合その間の値を計算する
        double sum = startConvTime;

        // startSecが-1の時は[-1,0]をsumが0になるようにサンプリング
        var animation = GetSpeedAnimation()!;
        for (; startSec < origStartSec;)
        {
            // ここで計算してキャッシュに保存する
            if (startSec >= 0)
            {
                for (int i = 0; i < context.SampleRate; i++)
                {
                    sum += animation.Interpolate(TimeSpan.FromSeconds((i / (double)context.SampleRate) + startSec)) /
                           100.0;
                }
            }

            _cache[++startSec] = sum;
        }

        var startInSamples = (int)(context.TimeRange.Start.TotalSeconds * context.SampleRate);
        for (int i = origStartSec * context.SampleRate; i < startInSamples; i++)
        {
            sum += animation.Interpolate(TimeSpan.FromSeconds(i / (double)context.SampleRate)) / 100.0;
        }

        // ここでsumはcontext.TimeRange.Startの変換後の時間を表す。つまりsourceTimeRangeの開始時間
        var sourceStartTime = TimeSpan.FromSeconds(sum / context.SampleRate);
        // Durationが1秒を超えると、その分のキャッシュが作成されないため非効率
        var durationInSamples = expectedOutputSampleCount;
        Span<double> speeds = new double[durationInSamples];
        for (int i = 0; i < durationInSamples; i++)
        {
            var value = animation.Interpolate(TimeSpan.FromSeconds((startInSamples + i) / (double)context.SampleRate)) /
                        100.0;
            speeds[i] = value;
            sum += value;
        }

        var sourceEndTime = TimeSpan.FromSeconds(sum / context.SampleRate);
        var actualSpeeds = speeds;
        var sourceTimeRange = TimeRange.FromRange(sourceStartTime, sourceEndTime);

        // Create input context with calculated source time range
        var inputContext = new AudioProcessContext(
            sourceTimeRange,
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        // Process with variable speed to get the expected output length
        return _processor!.ProcessBufferWithVariableSpeed(inputContext, actualSpeeds, expectedOutputSampleCount);
    }

    private TimeRange CalculateSourceTimeRange(TimeRange outputTimeRange, float speed)
    {
        var sourceStart = outputTimeRange.Start * speed;
        var sourceDuration = outputTimeRange.Duration * speed;

        return new TimeRange(sourceStart, sourceDuration);
    }

    private KeyFrameAnimation<float>? GetSpeedAnimation()
    {
        if (Target?.Animations == null || SpeedProperty == null)
            return null;

        return Target.Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id) as KeyFrameAnimation<float>;
    }

    protected override void Dispose(bool disposing)
    {
        _processor = null;
        base.Dispose(disposing);
    }

    private sealed class SpeedProcessor
    {
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly SpeedNode _speedNode;
        private readonly WdlResampler _rs;
        private float _currentSpeed = 1.0f;
        private TimeSpan lastSrcOffset;
        private const int BLOCK = 256;

        public SpeedProcessor(int sampleRate, int channels, SpeedNode speedNode)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _speedNode = speedNode;

            _rs = new WdlResampler();
            _rs.SetMode(interp: true, filtercnt: 0, sinc: true, sinc_size: 128, sinc_interpsize: 64);
            // _rs.SetMode(interp: true, filtercnt: 0, sinc: true, sinc_size: 64);
            _rs.SetFilterParms();
            _rs.SetFeedMode(false);
            _rs.SetRates(sampleRate, sampleRate);
        }

        private int Read(int srcOffset, float[] buffer, int offset, int count, AudioProcessContext context)
        {
            var newRange = new TimeRange(
                TimeSpan.FromSeconds(srcOffset / (double)_sampleRate) + context.TimeRange.Start,
                TimeSpan.FromSeconds(count / (double)_sampleRate));
            if (newRange.End > context.TimeRange.End)
            {
                // Console.WriteLine($"{newRange.End} > {context.TimeRange.End}");
                newRange = newRange.WithDuration(
                    TimeSpan.FromTicks(Math.Max((context.TimeRange.End - newRange.Start).Ticks, 0)));
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

        public AudioBuffer ProcessBufferWithVariableSpeed(
            AudioProcessContext context, ReadOnlySpan<double> speedCurve, int expectedOut)
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

            // speedCurve.Length: 44100
            // Sum: 42291.688
            Console.WriteLine($"Sum: {speedCurve.ToArray().Sum()}");
            var durationSamples = context.TimeRange.Duration.TotalSeconds * _sampleRate;
            // Last: 22050
            // 逆にcontext.TimeRangeの計算が間違っている気がする。
            // 早く減り過ぎ
            // 44100のサンプルを生み出すのに22050で生み出せるのはおかしい
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

                double needInF = srcPos + sumSpeed; // double 精度で次位置
                int needInInt = (int)Math.Floor(needInF); // 整数ぶんを今回読む
                // 変換前
                int wantFrames = needInInt - srcIndexFloor; // 追加で必要なフレーム数

                double vAvg = sumSpeed / framesThis;
                // Console.WriteLine(vAvg);
                double outRate = _sampleRate / vAvg; // ratio = vAvg
                _rs.SetRates(_sampleRate, outRate);
                float cutoff = 0.97f / (float)vAvg; // vAvg>1 なら Nyquist を下げる
                _rs.SetFilterParms(cutoff, 0.707f); // Q はそのまま

                float[] inBuf;
                int inOff;
                int willNeed = _rs.ResamplePrepare(framesThis, _channels, out inBuf, out inOff);

                if (wantFrames > willNeed) wantFrames = willNeed;

                // 本当に読む
                int got = Read(srcIndexFloor, inBuf, inOff, wantFrames, context);
                // srcIndexFloor += got;
                if (got < wantFrames)
                {
                    Console.WriteLine($"Clear: {got} < {wantFrames}");
                    Array.Clear(inBuf, inOff + got * _channels,
                        (wantFrames - got) * _channels);
                }

                int made = _rs.ResampleOut(dst,
                    framesDone * _channels,
                    willNeed, // 供給した入力フレーム数
                    framesThis, // 欲しい出力数
                    _channels);

                framesDone += made;
                srcIndexFloor += wantFrames;
                srcPos = needInF;
                // このメソッド呼び出しでの最後の出力がcontext.TimeRange.Endと同じになる必要があるが、この出力の方が大きくなってしまっている
                // フレームが過剰に供給されていることが考えられたが
                // Console.WriteLine(TimeSpan.FromSeconds(srcIndexFloor / (double)_sampleRate)+ context.TimeRange.Start);

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
    }
}
