using System.Buffers;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;
using NAudio.Dsp;

namespace Beutl.Audio.Graph.Nodes;

public sealed class SpeedNode : AudioNode
{
    // Processor for audio speed processing
    private SpeedProcessor? _processor;
    private int _lastSampleRate;
    private AudioNode? _lastInput;

    private readonly SpeedIntegrator _integrator;

    public SpeedNode()
    {
        _integrator = new SpeedIntegrator(0, () => _processor = null);
    }

    public IProperty<float>? Speed { get; set; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Variable speed node requires exactly one input.");

        // Calculate the expected output sample count based on the context's time range
        var expectedOutputSampleCount = context.GetSampleCount();

        var animation = Speed?.Animation;
        _integrator.EnsureCache(animation);
        _integrator.SampleRate = context.SampleRate;

        // Initialize processor if needed. A differential graph update can reuse this node but swap its
        // upstream (AudioContext.CreateSpeedNode -> ClearInputs); recreate the processor when the input
        // identity changes so the resampler does not carry filter history from a now-disconnected
        // source into the new stream.
        var input = Inputs[0];
        if (_processor == null || _lastSampleRate != context.SampleRate || !ReferenceEquals(_lastInput, input))
        {
            _processor = new SpeedProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
            _lastInput = input;
        }

        if (animation == null)
        {
            return ProcessStaticSpeed(context, expectedOutputSampleCount);
        }

        return ProcessAnimatedSpeed(context, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessStaticSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        float speed = (Speed?.CurrentValue ?? 100f) / 100f;
        // If speed is 1.0, use normal processing
        if (Math.Abs(speed - 1.0f) < float.Epsilon)
        {
            return Inputs[0].Process(context);
        }

        // The processor streams the source continuously across chunks; it derives the source read
        // range itself from the output context so the resampler is never re-seeked mid-stream.
        return _processor!.ProcessBuffer(context, speed, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessAnimatedSpeed(AudioProcessContext context, int expectedOutputSampleCount)
    {
        var animation = Speed?.Animation!;
        var keyFrameAnimation = (KeyFrameAnimation<float>)animation;

        // ClipNode 通過後の context.TimeRange.Start は要素ローカル時刻。
        // SpeedIntegrator.Integrate(t) は「時刻 0 から t までの累積積分」を返すため、
        // UseGlobalClock=true でグローバル時刻を渡す場合は要素開始前の積分 Integrate(ownerStart)
        // を差し引いて「要素開始からの累積」へ揃える必要がある。
        // per-sample 評価で使う GetAnimatedValue は常にグローバル時刻入力を前提とするため、
        // owner.TimeRange.Start を一律加算してグローバル時刻へ変換する。
        var ownerStart = Speed?.GetOwnerObject()?.TimeRange.Start ?? TimeSpan.Zero;
        TimeSpan sourceStartTime;
        if (keyFrameAnimation.UseGlobalClock)
        {
            sourceStartTime = _integrator.Integrate(context.TimeRange.Start + ownerStart, keyFrameAnimation)
                            - _integrator.Integrate(ownerStart, keyFrameAnimation);
        }
        else
        {
            sourceStartTime = _integrator.Integrate(context.TimeRange.Start, keyFrameAnimation);
        }

        // Per-sample speed buffer, sized to expectedOutputSampleCount and allocated every render —
        // rent from ArrayPool to avoid hot-path GC pressure. ProcessBufferWithVariableSpeed consumes
        // the span synchronously without retaining it, so the array is safe to return afterwards.
        var startInSamples = (int)(context.TimeRange.Start.TotalSeconds * context.SampleRate);
        double[] speedsArray = ArrayPool<double>.Shared.Rent(expectedOutputSampleCount);
        try
        {
            // The rented array can be larger than requested, so always slice before passing it.
            Span<double> speeds = speedsArray.AsSpan(0, expectedOutputSampleCount);
            for (int i = 0; i < expectedOutputSampleCount; i++)
            {
                speeds[i] = animation.GetAnimatedValue(
                    ownerStart + TimeSpan.FromSeconds((startInSamples + i) / (double)context.SampleRate)) / 100.0;
            }

            // The processor streams the source continuously across chunks; sourceStartTime only seeds
            // the read cursor on the first chunk / after a seek. context supplies the sampler and the
            // original time range for the per-read sub-contexts.
            return _processor!.ProcessBufferWithVariableSpeed(
                context, speeds, expectedOutputSampleCount, sourceStartTime.TotalSeconds);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(speedsArray);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _integrator.Dispose();
        _processor = null;
        _lastInput = null;
        base.Dispose(disposing);
    }

    private sealed class SpeedProcessor
    {
        private const int BLOCK = 256;

        // A chunk whose output start lands within this many samples of where the previous chunk ended
        // is treated as a continuation; anything further is a seek (scrub / loop / restart).
        //
        // The tolerance is deliberately tiny (samples, not milliseconds). During contiguous playback the
        // player advances the chunk start by an exact TimeSpan (PlayerViewModel: cur += 1s) and
        // _nextOutputStart advances by expectedOut/sampleRate, so the two agree to the bit — there is no
        // timing jitter to absorb. The couple-of-samples slack only covers Ceiling rounding on a
        // fractional-duration final chunk. Widening this to tens of milliseconds would do the opposite of
        // helping: a genuine short seek would be mistaken for a continuation and keep playing from the
        // stale source position instead of re-anchoring.
        private const double SeekToleranceSamples = 2.0;

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly SpeedNode _speedNode;
        private readonly WdlResampler _rs;
        private float _currentSpeed = 1.0f;

        // Continuous-streaming state, persisted across Process calls (chunks). The resampler retains
        // filter history between chunks, so the source must be fed as one unbroken stream. Re-seeking
        // the source every chunk (the previous behaviour) desynchronised the source read position from
        // that retained history and produced an audible click at every chunk boundary.
        private long _srcReadPos;        // absolute next source sample to feed, in the source timeline
        private double _nextOutputStart; // expected output start (seconds) of the next contiguous chunk
        private bool _initialized;

        public SpeedProcessor(int sampleRate, int channels, SpeedNode speedNode)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _speedNode = speedNode;

            _rs = new WdlResampler();
            _rs.SetMode(interp: true, filtercnt: 0, sinc: true, sinc_size: 128, sinc_interpsize: 64);
            _rs.SetFilterParms();
            _rs.SetFeedMode(false);
            _rs.SetRates(sampleRate, sampleRate);
        }

        // Decides whether this chunk continues the previous stream or is a seek. On a seek the
        // resampler is reset and the read cursor is re-anchored to the freshly computed source start.
        // Returns true when a seek occurred.
        private bool BeginStream(double outputStartSeconds, double sourceStartSeconds)
        {
            bool seek = !_initialized
                || Math.Abs(outputStartSeconds - _nextOutputStart) * _sampleRate > SeekToleranceSamples;
            if (seek)
            {
                _rs.Reset();
                _srcReadPos = (long)Math.Round(sourceStartSeconds * _sampleRate);
                _initialized = true;
            }

            return seek;
        }

        // Reads exactly the source samples the resampler asked for, continuing from the persistent
        // cursor so the stream never jumps between chunks. Advances the cursor by what was actually
        // produced (fewer than requested only at the true end of the source) and returns that count.
        private int Read(float[] buffer, int interleavedOffset, int count, AudioProcessContext context)
        {
            if (count <= 0)
                return 0;

            // _srcReadPos counts whole source samples, but it reaches the input as a TimeSpan and the
            // source converts back by truncation (e.g. SourceNode: (int)(seconds * sampleRate)). Bias
            // the start by half a sample so the floating-point round-trip lands back on _srcReadPos
            // rather than occasionally on _srcReadPos - 1.
            var range = new TimeRange(
                TimeSpan.FromSeconds((_srcReadPos + 0.5) / _sampleRate),
                TimeSpan.FromSeconds(count / (double)_sampleRate));
            var subContext = new AudioProcessContext(
                range,
                _sampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

            var result = _speedNode.Inputs[0].Process(subContext);
            var leftData = result.GetChannelData(0);
            var rightData = result.GetChannelData(1);
            int samplesToRead = Math.Min(count, result.SampleCount);
            for (int i = 0; i < samplesToRead; i++)
            {
                buffer[interleavedOffset + i * _channels] = leftData[i];
                buffer[interleavedOffset + i * _channels + 1] = rightData[i];
            }

            _srcReadPos += samplesToRead;
            return samplesToRead;
        }

        public AudioBuffer ProcessBuffer(AudioProcessContext context, float speed, int expectedOut)
        {
            double outputStart = context.TimeRange.Start.TotalSeconds;
            bool seek = BeginStream(outputStart, outputStart * speed);

            // Re-anchoring the rate after a seek (the resampler was just reset) or whenever the
            // constant speed changes. Never Reset() outside of a seek: that zero-fills the filter
            // history and momentarily silences a stream that is otherwise continuous.
            if (seek || Math.Abs(_currentSpeed - speed) > 1e-4f)
            {
                _currentSpeed = speed;
                _rs.SetRates(_sampleRate, _sampleRate / speed);
            }

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] dst = new float[expectedOut * _channels];

            int framesDone = 0;
            while (framesDone < expectedOut)
            {
                int want = _rs.ResamplePrepare(expectedOut - framesDone, _channels, out float[] inBuf, out int inOff);
                int got = Read(inBuf, inOff, want, context);

                int made = _rs.ResampleOut(dst, framesDone * _channels, got, expectedOut - framesDone, _channels);

                // No more output and no more input means the source is exhausted; the tail-fill below
                // pads the remainder. (got > 0 with made == 0 just means the resampler needs more
                // lookahead, so keep feeding.)
                if (made == 0 && got == 0)
                    break;

                framesDone += made;
            }

            WriteAndPad(output, dst, framesDone, expectedOut);
            _nextOutputStart = outputStart + (double)expectedOut / _sampleRate;
            return output;
        }

        public AudioBuffer ProcessBufferWithVariableSpeed(
            AudioProcessContext context, ReadOnlySpan<double> speedCurve, int expectedOut, double sourceStartSeconds)
        {
            double outputStart = context.TimeRange.Start.TotalSeconds;
            BeginStream(outputStart, sourceStartSeconds);

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] dst = new float[expectedOut * _channels];

            // Same resampler-driven streaming loop as the constant-speed path, but the rate is updated
            // per block from the average of the per-sample speed curve. The resampler's ResamplePrepare
            // is the single source of truth for how many source frames to feed, and exactly that many
            // are read and reported back via ResampleOut — there is no separate hand-rolled source
            // cursor to drift out of sync, and no stale frames are ever passed off as real input.
            int framesDone = 0;
            while (framesDone < expectedOut)
            {
                int framesThis = Math.Min(BLOCK, expectedOut - framesDone);

                double sumSpeed = 0.0;
                for (int i = 0; i < framesThis; i++)
                    sumSpeed += speedCurve[framesDone + i];

                double vAvg = sumSpeed / framesThis;
                _rs.SetRates(_sampleRate, _sampleRate / vAvg);
                float cutoff = 0.97f / (float)vAvg; // vAvg > 1 lowers Nyquist to avoid aliasing
                _rs.SetFilterParms(cutoff, 0.707f);

                int want = _rs.ResamplePrepare(framesThis, _channels, out float[] inBuf, out int inOff);
                int got = Read(inBuf, inOff, want, context);

                int made = _rs.ResampleOut(dst, framesDone * _channels, got, framesThis, _channels);

                if (made == 0 && got == 0)
                    break;

                framesDone += made;
            }

            WriteAndPad(output, dst, framesDone, expectedOut);
            _nextOutputStart = outputStart + (double)expectedOut / _sampleRate;
            return output;
        }

        // De-interleaves the produced frames into the output buffer and pads any shortfall (source
        // exhausted) with the last produced value to avoid a hard edge.
        private void WriteAndPad(AudioBuffer output, float[] dst, int framesDone, int expectedOut)
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                var chData = output.GetChannelData(ch);
                for (int n = 0; n < framesDone; n++)
                    chData[n] = dst[n * _channels + ch];

                float tail = framesDone > 0 ? chData[framesDone - 1] : 0f;
                for (int n = framesDone; n < expectedOut; n++)
                    chData[n] = tail;
            }
        }
    }
}
