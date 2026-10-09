using Beutl.Media;
using NAudio.Dsp;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class SpeedNode
{
    private sealed partial class SpeedProcessor
    {
        private const int BLOCK = 256;

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly SpeedNode _speedNode;
        private readonly WdlResampler _rs;
        private ResamplingMode _resamplingMode;
        private float _currentSpeed = 1.0f;

        private readonly AudioSourceStream _stream;

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
            _stream = new AudioSourceStream(sampleRate, channels, speedNode._mapping.MapOutputTimeToSource);
        }

        public bool CanPassThroughDrain => _stream.CanPassThroughDrain;

        public void TrackPassthrough(AudioProcessContext context, int sampleCount)
        {
            _rs.Reset();
            _stream.TrackPassthrough(context, sampleCount);
            _currentSpeed = 1f;
            _resamplingMode = ResamplingMode.Passthrough;
        }

        private void ConfigureStaticResampling(float speed)
        {
            _rs.SetRates(_sampleRate, _sampleRate / speed);
            _rs.SetFilterParms();
            _currentSpeed = speed;
            _resamplingMode = ResamplingMode.Static;
        }

        private void ConfigureVariableResampling(double speed)
        {
            _rs.SetRates(_sampleRate, _sampleRate / speed);
            float cutoff = 0.97f / (float)speed;
            _rs.SetFilterParms(cutoff, 0.707f);
            _currentSpeed = (float)speed;
            _resamplingMode = ResamplingMode.Variable;
        }

        // Decides whether this chunk continues the stream or is a seek; on a seek the resampler is
        // reset and the read cursor re-anchored to the computed source start. forceReanchor lets the
        // caller declare a discontinuity the output-time comparison cannot see (e.g. a static-speed
        // change across contiguous chunks). Returns true on a seek.
        private bool BeginStream(double outputStartSeconds, double sourceStartSeconds, bool forceReanchor = false)
        {
            bool seek = _stream.Begin(outputStartSeconds, sourceStartSeconds, forceReanchor);
            if (seek)
                _rs.Reset();
            return seek;
        }

        // Reads exactly the requested source samples, continuing from the persistent cursor so the
        // stream never jumps between chunks. Advances the cursor by what was actually produced (short
        // only at end-of-source) and returns that count.
        private int Read(Span<float> buffer, AudioProcessContext context, bool draining)
            => _stream.Read(buffer, _speedNode.Inputs[0], context, draining);

        public AudioBuffer ProcessBuffer(
            AudioProcessContext context,
            float speed,
            int expectedOut,
            bool draining,
            bool forceReanchor)
        {
            double outputStart = context.TimeRange.Start.TotalSeconds;

            // A static-speed change across contiguous chunks is a source-position discontinuity that
            // BeginStream's output-time comparison cannot see: output stays contiguous, but the source cursor
            // (tracking the old speed) no longer maps to outputStart. Force a re-anchor to outputStart *
            // speed. _stream.IsInitialized gates this so the first-chunk anchor still uses the normal seek path.
            bool configurationChanged = _resamplingMode != ResamplingMode.Static
                || Math.Abs(_currentSpeed - speed) > 1e-4f;
            bool seek = BeginStream(
                outputStart,
                outputStart * speed,
                forceReanchor: _stream.IsInitialized
                    && !draining
                    && (configurationChanged || forceReanchor));

            // Re-set the rate after a seek (resampler just reset) or when the constant speed changes.
            // Never Reset() outside a seek: that zero-fills filter history and silences a continuous
            // Draining keeps the cursor at the upstream end while changing only the resampling rate.
            if (seek || configurationChanged)
            {
                ConfigureStaticResampling(speed);
            }

            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            try
            {
                float[] dst = new float[expectedOut * _channels];

                int framesDone = 0;
                while (framesDone < expectedOut)
                {
                    int want = _rs.ResamplePrepare(
                        expectedOut - framesDone,
                        _channels,
                        out Span<float> inBuf);
                    int got = Read(inBuf[..(want * _channels)], context, draining) / _channels;

                    int made = _rs.ResampleOut(
                        dst.AsSpan(framesDone * _channels, (expectedOut - framesDone) * _channels),
                        got,
                        expectedOut - framesDone,
                        _channels);

                    // No output and no input means the source is exhausted; the tail-fill below pads the
                    // rest. (got > 0 with made == 0 just means the resampler needs more lookahead.)
                    if (made == 0 && got == 0)
                        break;

                    framesDone += made;
                }

                WriteAndPad(output, dst, framesDone, expectedOut);
                // Advance only on full success.
                _stream.Complete(context, expectedOut);
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
            AudioProcessContext context,
            ReadOnlySpan<double> speedCurve,
            int expectedOut,
            double sourceStartSeconds,
            bool draining,
            bool forceReanchor)
        {
            double outputStart = context.TimeRange.Start.TotalSeconds;
            BeginStream(
                outputStart,
                sourceStartSeconds,
                forceReanchor: _stream.IsInitialized && !draining && forceReanchor);

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
                    ConfigureVariableResampling(vAvg);

                    int want = _rs.ResamplePrepare(framesThis, _channels, out Span<float> inBuf);
                    int got = Read(inBuf[..(want * _channels)], context, draining) / _channels;

                    int made = _rs.ResampleOut(
                        dst.AsSpan(framesDone * _channels, framesThis * _channels),
                        got,
                        framesThis,
                        _channels);

                    if (made == 0 && got == 0)
                        break;

                    framesDone += made;
                }

                WriteAndPad(output, dst, framesDone, expectedOut);
                // Advance only on full success.
                _stream.Complete(context, expectedOut);
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private enum ResamplingMode
        {
            Unconfigured,
            Passthrough,
            Static,
            Variable,
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
