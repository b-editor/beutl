using Beutl.Engine;
using Beutl.Media;
using NAudio.Dsp;

namespace Beutl.Audio.Graph.Nodes;

public sealed class PitchNode : AudioNode
{
    private PitchProcessor? _processor;
    private int _lastSampleRate;

    public IProperty<float>? Semitones { get; set; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Pitch node requires exactly one input.");

        var expectedOutputSampleCount = context.GetSampleCount();

        // Initialize processor if needed
        if (_processor == null || _lastSampleRate != context.SampleRate)
        {
            _processor = new PitchProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
        }

        var animation = Semitones?.Animation;
        if (animation != null)
        {
            return ProcessAnimated(context, expectedOutputSampleCount);
        }

        return ProcessStatic(context, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessStatic(AudioProcessContext context, int expectedOutputSampleCount)
    {
        float semitones = Semitones?.CurrentValue ?? 0f;

        // If semitones is 0, no pitch change needed
        if (Math.Abs(semitones) < float.Epsilon)
        {
            return Inputs[0].Process(context);
        }

        double pitchRatio = Math.Pow(2.0, semitones / 12.0);

        // Calculate source time range: need ratio times more (or less) audio data
        var sourceTimeRange = CalculateSourceTimeRange(context.TimeRange, pitchRatio);

        var inputContext = new AudioProcessContext(
            sourceTimeRange,
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        return _processor!.ProcessBuffer(inputContext, pitchRatio, expectedOutputSampleCount);
    }

    private AudioBuffer ProcessAnimated(AudioProcessContext context, int expectedOutputSampleCount)
    {
        return _processor!.ProcessBufferAnimated(
            context, Semitones!, expectedOutputSampleCount);
    }

    private static TimeRange CalculateSourceTimeRange(TimeRange outputTimeRange, double pitchRatio)
    {
        // To pitch up (ratio > 1): need MORE source data
        // To pitch down (ratio < 1): need LESS source data
        var sourceDuration = outputTimeRange.Duration * pitchRatio;
        return new TimeRange(outputTimeRange.Start, sourceDuration);
    }

    protected override void Dispose(bool disposing)
    {
        _processor = null;
        base.Dispose(disposing);
    }

    private sealed class PitchProcessor
    {
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly PitchNode _pitchNode;
        private readonly WdlResampler _rs;
        private double _currentRatio = 1.0;
        private const int BLOCK = 256;

        public PitchProcessor(int sampleRate, int channels, PitchNode pitchNode)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _pitchNode = pitchNode;

            _rs = new WdlResampler();
            _rs.SetMode(interp: true, filtercnt: 0, sinc: true, sinc_size: 128, sinc_interpsize: 64);
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
                newRange = newRange.WithDuration(
                    TimeSpan.FromTicks(Math.Max((context.TimeRange.End - newRange.Start).Ticks, 0)));
            }

            var newContext = new AudioProcessContext(
                newRange,
                _sampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

            var result = _pitchNode.Inputs[0].Process(newContext);
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

        public AudioBuffer ProcessBuffer(AudioProcessContext context, double pitchRatio, int expectedOut)
        {
            // pitchRatio > 1: pitch up, read more source, resample down
            // pitchRatio < 1: pitch down, read less source, resample up
            if (Math.Abs(_currentRatio - pitchRatio) > 1e-6)
            {
                _currentRatio = pitchRatio;
                _rs.SetRates(_sampleRate, _sampleRate / pitchRatio);
            }

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] dst = new float[expectedOut * _channels];

            int framesDone = 0;
            int srcFramesRead = 0;

            while (framesDone < expectedOut)
            {
                float[] inBuf;
                int inOff;

                int want = _rs.ResamplePrepare(expectedOut - framesDone, _channels, out inBuf, out inOff);

                int got = Read(srcFramesRead, inBuf, inOff / _channels, want, context);
                srcFramesRead += got;

                int made = _rs.ResampleOut(dst, framesDone * _channels, got, expectedOut - framesDone, _channels);

                if (made == 0)
                {
                    made = _rs.ResampleOut(dst, framesDone * _channels, 0, expectedOut - framesDone, _channels);
                    if (made == 0) break;
                }

                framesDone += made;
            }

            for (int ch = 0; ch < _channels; ch++)
            {
                var chData = output.GetChannelData(ch);
                for (int n = 0; n < framesDone; n++)
                    chData[n] = dst[n * _channels + ch];

                float tail = framesDone > 0 ? chData[framesDone - 1] : 0f;
                for (int n = framesDone; n < expectedOut; n++)
                    chData[n] = tail;
            }

            return output;
        }

        public AudioBuffer ProcessBufferAnimated(
            AudioProcessContext context, IProperty<float> semitonesProperty, int expectedOut)
        {
            int framesDone = 0;
            int srcIndexFloor = 0;
            double srcPos = 0.0;

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] dst = new float[expectedOut * _channels];

            // Sample semitone values and convert to pitch ratios
            Span<double> pitchRatios = new double[expectedOut];
            Span<float> semitonesBuffer = new float[Math.Min(expectedOut, 8192)];

            int sampled = 0;
            while (sampled < expectedOut)
            {
                int chunkSize = Math.Min(semitonesBuffer.Length, expectedOut - sampled);
                var chunkStart = context.GetTimeForSample(sampled);
                var chunkEnd = context.GetTimeForSample(sampled + chunkSize);
                var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

                context.AnimationSampler.SampleBuffer(
                    semitonesProperty, chunkRange, context.SampleRate, semitonesBuffer[..chunkSize]);

                for (int i = 0; i < chunkSize; i++)
                    pitchRatios[sampled + i] = Math.Pow(2.0, semitonesBuffer[i] / 12.0);

                sampled += chunkSize;
            }

            // Calculate total source samples needed
            double totalSourceSamples = 0;
            for (int i = 0; i < expectedOut; i++)
                totalSourceSamples += pitchRatios[i];

            // Build source time range
            var sourceTimeRange = new TimeRange(
                context.TimeRange.Start,
                TimeSpan.FromSeconds(totalSourceSamples / _sampleRate));

            var inputContext = new AudioProcessContext(
                sourceTimeRange,
                _sampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

            while (framesDone < expectedOut)
            {
                int framesThis = Math.Min(BLOCK, expectedOut - framesDone);

                // Calculate average pitch ratio for this block
                double sumRatio = 0.0;
                for (int i = 0; i < framesThis; i++)
                    sumRatio += pitchRatios[framesDone + i];

                double avgRatio = sumRatio / framesThis;

                double needInF = srcPos + sumRatio;
                int needInInt = (int)Math.Floor(needInF);
                int wantFrames = needInInt - srcIndexFloor;

                double outRate = _sampleRate / avgRatio;
                _rs.SetRates(_sampleRate, outRate);

                if (avgRatio > 1.0)
                {
                    float cutoff = 0.97f / (float)avgRatio;
                    _rs.SetFilterParms(cutoff, 0.707f);
                }
                else
                {
                    _rs.SetFilterParms();
                }

                float[] inBuf;
                int inOff;
                int willNeed = _rs.ResamplePrepare(framesThis, _channels, out inBuf, out inOff);

                if (wantFrames > willNeed) wantFrames = willNeed;

                int got = Read(srcIndexFloor, inBuf, inOff, wantFrames, inputContext);

                if (got < wantFrames)
                {
                    Array.Clear(inBuf, inOff + got * _channels, (wantFrames - got) * _channels);
                }

                int made = _rs.ResampleOut(dst, framesDone * _channels, willNeed, framesThis, _channels);

                framesDone += made;
                srcIndexFloor += wantFrames;
                srcPos = needInF;
            }

            for (int ch = 0; ch < _channels; ch++)
            {
                var chData = output.GetChannelData(ch);
                for (int n = 0; n < framesDone; n++)
                    chData[n] = dst[n * _channels + ch];

                float tail = framesDone > 0 ? chData[framesDone - 1] : 0f;
                for (int n = framesDone; n < expectedOut; n++)
                    chData[n] = tail;
            }

            return output;
        }
    }
}
