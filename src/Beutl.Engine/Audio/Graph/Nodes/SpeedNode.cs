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
    private List<AudioNode>? _upstreamSnapshot;

    private readonly SpeedIntegrator _integrator;

    public SpeedNode()
    {
        _integrator = new SpeedIntegrator(0, () => _processor = null);
    }

    public IProperty<float>? Speed { get; set; }

    // Known limitation (same class as ResampleNode's): no Flush override. Process streams its input
    // through a resampler at a derived source position/rate; the inherited flush instead forwards the
    // output context straight upstream, so a latency-bearing node placed UPSTREAM of this SpeedNode
    // would see a discontinuity on flush, re-anchor, and drop its tail. The built-in Sound chain puts
    // SpeedNode before the effects, so a limiter is always downstream — this only bites a custom graph
    // that time-stretches after a latency-bearing node; a re-anchoring Flush override is the follow-up.
    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Variable speed node requires exactly one input.");

        // Calculate the expected output sample count based on the context's time range
        var expectedOutputSampleCount = context.GetSampleCount();

        var animation = Speed?.Animation;
        _integrator.EnsureCache(animation);
        _integrator.SampleRate = context.SampleRate;

        // Recreate the processor when the upstream changes, so the resampler does not carry filter
        // history (or a stale read cursor) from a disconnected source into the new stream. Comparing
        // Inputs[0] alone is not enough: the graph reuses one ResampleNode keyed by sample rate, so
        // Inputs[0] can be unchanged while the source feeding it is recreated. Snapshot the whole
        // transitive upstream and compare by identity. Capture unconditionally so the first chunk seeds
        // the snapshot and a contiguous second chunk is not mistaken for a swap.
        bool upstreamChanged = UpstreamChangedAndCapture();
        if (_processor == null || _lastSampleRate != context.SampleRate || upstreamChanged)
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
        float speed = (Speed?.CurrentValue ?? 100f) / 100f;
        // If speed is 1.0, use normal processing
        if (Math.Abs(speed - 1.0f) < float.Epsilon)
        {
            return Inputs[0].Process(context);
        }

        // The processor streams the source continuously, deriving the read range itself so the
        // resampler is never re-seeked mid-stream.
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
        var startInSamples = AudioMath.TimeToSampleIndex(context.TimeRange.Start, context.SampleRate);
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

            // sourceStartTime only seeds the read cursor on the first chunk / after a seek; context
            // supplies the sampler and original time range for the per-read sub-contexts.
            return _processor!.ProcessBufferWithVariableSpeed(
                context, speeds, expectedOutputSampleCount, sourceStartTime.TotalSeconds);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(speedsArray);
        }
    }

    // Snapshots the transitive upstream nodes (depth-first, deduplicated) and reports whether they
    // differ from the previous snapshot by identity. Audio graphs are tiny and this runs once per
    // chunk, so the walk is negligible.
    private bool UpstreamChangedAndCapture()
    {
        var current = new List<AudioNode>();
        CollectUpstream(this, current, new HashSet<AudioNode>());

        bool changed = _upstreamSnapshot is null || _upstreamSnapshot.Count != current.Count;
        if (!changed)
        {
            for (int i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(_upstreamSnapshot![i], current[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
            _upstreamSnapshot = current;

        return changed;
    }

    private static void CollectUpstream(AudioNode node, List<AudioNode> acc, HashSet<AudioNode> visited)
    {
        foreach (var input in node.Inputs)
        {
            // Dedupe so a diamond upstream cannot blow up the walk or perturb the snapshot.
            if (!visited.Add(input))
                continue;

            acc.Add(input);
            CollectUpstream(input, acc, visited);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _integrator.Dispose();
        _processor = null;
        _upstreamSnapshot = null;
        base.Dispose(disposing);
    }

    private sealed class SpeedProcessor
    {
        private const int BLOCK = 256;

        // A chunk starting within this many samples of where the previous one ended is a continuation;
        // anything further is a seek (scrub / loop / restart). Kept tiny on purpose: contiguous playback
        // advances exactly (no jitter to absorb), so this slack only covers Ceiling rounding on a
        // fractional final chunk. Widening it would mistake a short seek for a continuation.
        private const double SeekToleranceSamples = 2.0;

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly SpeedNode _speedNode;
        private readonly WdlResampler _rs;
        private float _currentSpeed = 1.0f;

        // Continuous-streaming state, persisted across chunks. The resampler retains filter history
        // between chunks, so the source must be fed as one unbroken stream — re-seeking every chunk
        // desynchronised the read position from that history and clicked at each boundary.
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

        // Decides whether this chunk continues the stream or is a seek; on a seek the resampler is
        // reset and the read cursor re-anchored to the computed source start. forceReanchor lets the
        // caller declare a discontinuity the output-time comparison cannot see (e.g. a static-speed
        // change across contiguous chunks). Returns true on a seek.
        private bool BeginStream(double outputStartSeconds, double sourceStartSeconds, bool forceReanchor = false)
        {
            bool seek = !_initialized
                || forceReanchor
                || Math.Abs(outputStartSeconds - _nextOutputStart) * _sampleRate > SeekToleranceSamples;
            if (seek)
            {
                _rs.Reset();
                _srcReadPos = (long)Math.Round(sourceStartSeconds * _sampleRate);
                _initialized = true;
            }

            return seek;
        }

        // Reads exactly the requested source samples, continuing from the persistent cursor so the
        // stream never jumps between chunks. Advances the cursor by what was actually produced (short
        // only at end-of-source) and returns that count.
        private int Read(float[] buffer, int interleavedOffset, int count, AudioProcessContext context)
        {
            if (count <= 0)
                return 0;

            // _srcReadPos is whole samples, but reaches the input as a TimeSpan that the source
            // truncates back (e.g. (int)(seconds * sampleRate)). Bias by half a sample so the
            // round-trip lands on _srcReadPos rather than occasionally _srcReadPos - 1.
            var range = new TimeRange(
                TimeSpan.FromSeconds((_srcReadPos + 0.5) / _sampleRate),
                TimeSpan.FromSeconds(count / (double)_sampleRate));
            var subContext = new AudioProcessContext(
                range,
                _sampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

            // Read consumes the child buffer fully into the interleaved span and never returns it, so
            // dispose its pooled MemoryPool<float> lease here; otherwise every resampler iteration leaks
            // one input buffer (matches the disposal contract the sibling audio nodes already honor).
            using var result = _speedNode.Inputs[0].Process(subContext);
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

            // A static-speed change across contiguous chunks is a source-position discontinuity that
            // BeginStream's output-time comparison cannot see: output stays contiguous, but _srcReadPos
            // (tracking the old speed) no longer maps to outputStart. Force a re-anchor to outputStart *
            // speed. _initialized gates this so the first-chunk anchor still uses the normal seek path.
            bool speedChanged = _initialized && Math.Abs(_currentSpeed - speed) > 1e-4f;
            bool seek = BeginStream(outputStart, outputStart * speed, speedChanged);

            // Re-set the rate after a seek (resampler just reset) or when the constant speed changes.
            // Never Reset() outside a seek: that zero-fills filter history and silences a continuous
            // stream — a static-speed change is promoted to a seek above, so its reset is intentional.
            if (seek || speedChanged)
            {
                _currentSpeed = speed;
                _rs.SetRates(_sampleRate, _sampleRate / speed);
            }

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            try
            {
                float[] dst = new float[expectedOut * _channels];

                int framesDone = 0;
                while (framesDone < expectedOut)
                {
                    int want = _rs.ResamplePrepare(expectedOut - framesDone, _channels, out float[] inBuf, out int inOff);
                    int got = Read(inBuf, inOff, want, context);

                    int made = _rs.ResampleOut(dst, framesDone * _channels, got, expectedOut - framesDone, _channels);

                    // No output and no input means the source is exhausted; the tail-fill below pads the
                    // rest. (got > 0 with made == 0 just means the resampler needs more lookahead.)
                    if (made == 0 && got == 0)
                        break;

                    framesDone += made;
                }

                WriteAndPad(output, dst, framesDone, expectedOut);
                // Advance only on full success.
                _nextOutputStart = outputStart + (double)expectedOut / _sampleRate;
                return output;
            }
            catch
            {
                // Dispose the output the caller never received rather than leak it.
                output.Dispose();
                throw;
            }
        }

        public AudioBuffer ProcessBufferWithVariableSpeed(
            AudioProcessContext context, ReadOnlySpan<double> speedCurve, int expectedOut, double sourceStartSeconds)
        {
            double outputStart = context.TimeRange.Start.TotalSeconds;
            BeginStream(outputStart, sourceStartSeconds);

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            try
            {
                float[] dst = new float[expectedOut * _channels];

                // Same streaming loop as the constant-speed path, but the rate is updated per block from
                // the average of the speed curve. ResamplePrepare is the single source of truth for how
                // many source frames to feed, so there is no hand-rolled cursor to drift out of sync.
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
                // Advance only on full success.
                _nextOutputStart = outputStart + (double)expectedOut / _sampleRate;
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        // De-interleaves the produced frames into the output and pads any shortfall (source exhausted)
        // with the last value to avoid a hard edge.
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
