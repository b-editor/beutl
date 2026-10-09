using System.Buffers;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class TimeStretchNode
{
    private sealed class TimeStretchProcessor
    {
        private const int BLOCK = 256;
        private const int TimeStretchFeedFrames = 1024;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly TimeStretchNode _node;
        private readonly AudioSourceStream _stream;
        private readonly WsolaTimeStretcher _stretcher;
        private float? _staticSpeed;
        private bool _timeStretchFlushed;
        private long? _timeStretchInputEnd;
        private bool _timeStretchDraining;

        public TimeStretchProcessor(int sampleRate, int channels, TimeStretchNode node)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _node = node;
            _stream = new AudioSourceStream(sampleRate, channels, node._mapping.MapOutputTimeToSource);
            _stretcher = new WsolaTimeStretcher(sampleRate, channels);
        }

        public bool CanPassThroughDrain => _stream.CanPassThroughDrain;

        private void Reset()
        {
            _stretcher.Clear();
            _timeStretchFlushed = false;
            _timeStretchInputEnd = null;
            _timeStretchDraining = false;
        }

        public void TrackPassthrough(AudioProcessContext context, int sampleCount)
        {
            Reset();
            _stream.TrackPassthrough(context, sampleCount);
            _staticSpeed = 1f;
        }

        private bool BeginStream(double outputStartSeconds, double sourceStartSeconds, bool forceReanchor = false)
        {
            bool seek = _stream.Begin(outputStartSeconds, sourceStartSeconds, forceReanchor);
            if (seek)
                Reset();
            return seek;
        }

        private int Read(Span<float> buffer, AudioProcessContext context, bool draining)
            => _stream.Read(buffer, _node.Inputs[0], context, draining, _timeStretchInputEnd);

        public AudioBuffer ProcessBuffer(AudioProcessContext context, float speed, int expectedOut, bool draining, bool forceReanchor)
        {
            double outputStart = context.TimeRange.Start.TotalSeconds;
            bool configurationChanged = _staticSpeed is null || Math.Abs(_staticSpeed.Value - speed) > 1e-4f;
            BeginStream(outputStart, outputStart * speed,
                forceReanchor: _stream.IsInitialized && !draining && (configurationChanged || forceReanchor));
            _staticSpeed = speed;
            return ProcessTimeStretch(context, default, speed, expectedOut, draining);
        }

        public AudioBuffer ProcessBufferWithVariableSpeed(AudioProcessContext context, ReadOnlySpan<double> speeds,
            int expectedOut, double sourceStartSeconds, bool draining, bool forceReanchor)
        {
            BeginStream(context.TimeRange.Start.TotalSeconds, sourceStartSeconds,
                forceReanchor: _stream.IsInitialized && !draining && forceReanchor);
            _staticSpeed = null;
            return ProcessTimeStretch(context, speeds, 1f, expectedOut, draining);
        }

        private AudioBuffer ProcessTimeStretch(
            AudioProcessContext context,
            ReadOnlySpan<double> speedCurve,
            float speed,
            int expectedOut,
            bool draining)
        {
            double? end = draining ? null : _node.Inputs[0].GetFiniteSourceEndSample(_sampleRate);
            if (!draining && context.ProcessEndTime is { } terminal)
            {
                double terminalSample = _node._mapping.MapOutputTimeToSource(terminal).TotalSeconds * _sampleRate;
                end = end is { } finite ? Math.Min(finite, terminalSample) : terminalSample;
            }
            if (draining && !_timeStretchDraining)
            {
                // Analysis lookahead belongs to live processing, not to the held upstream tail.
                // The live boundary kept the upstream state at its terminal sample; start a fresh
                // drain stream there and consume only the latency that the upstream still retains.
                _stretcher.Clear();
                _timeStretchFlushed = false;
                _timeStretchDraining = true;
                int latency = _node.GetMaxInputLatency(_sampleRate, drain: true);
                _timeStretchInputEnd = latency == int.MaxValue ? null : checked(_stream.Position + latency);
            }
            long? inputEnd = _timeStretchInputEnd;
            if (end is { } value && double.IsFinite(value))
            {
                double rounded = Math.Round(value);
                inputEnd = checked((long)(Math.Abs(value - rounded) < 1e-6 ? rounded : Math.Ceiling(value)));
            }
            else if (!draining)
            {
                inputEnd = null;
            }
            bool boundaryShrank = inputEnd is { } newEnd
                && (_timeStretchInputEnd is { } oldEnd ? newEnd < oldEnd : _stream.Position > newEnd);
            if (!draining && (_timeStretchDraining || boundaryShrank
                || (_timeStretchFlushed && _timeStretchInputEnd is { } previousEnd
                    && (inputEnd is null || inputEnd > previousEnd))))
            {
                // Differential updates can change a downstream clip without replacing this processor.
                // Re-anchor at the playback position to discard post-trim lookahead or reopen a
                // finished stream, since lookahead or draining may have moved the read cursor ahead.
                BeginStream(context.TimeRange.Start.TotalSeconds,
                    _node._mapping.MapOutputTimeToSource(context.TimeRange.Start).TotalSeconds,
                    forceReanchor: true);
            }
            _timeStretchInputEnd = inputEnd;
            var output = new AudioBuffer(_sampleRate, _channels, expectedOut);
            float[] inputArray = ArrayPool<float>.Shared.Rent(TimeStretchFeedFrames * _channels);
            float[] outputArray = ArrayPool<float>.Shared.Rent(BLOCK * _channels);
            try
            {
                Span<float> input = inputArray.AsSpan(0, TimeStretchFeedFrames * _channels);
                int framesDone = 0;
                while (framesDone < expectedOut)
                {
                    int framesThis = Math.Min(BLOCK, expectedOut - framesDone);
                    ReadOnlySpan<double> curve = speedCurve.IsEmpty
                        ? default
                        : speedCurve.Slice(framesDone, framesThis);
                    _stretcher.Tempo = speed;
                    int made = _stretcher.ReceiveSamples(
                        outputArray.AsSpan(0, framesThis * _channels), framesThis, curve);
                    if (made == 0)
                    {
                        if (_timeStretchFlushed)
                            break;

                        int got = Read(input, context, draining) / _channels;
                        if (got > 0)
                        {
                            _stretcher.PutSamples(input[..(got * _channels)], got);
                        }
                        if (got == 0 || (_timeStretchInputEnd is { } terminalSample && _stream.Position >= terminalSample))
                        {
                            // A short/exhausted source must release its final processing window once.
                            _stretcher.Flush();
                            _timeStretchFlushed = true;
                        }
                        continue;
                    }

                    for (int ch = 0; ch < _channels; ch++)
                    {
                        Span<float> destination = output.GetChannelData(ch).Slice(framesDone, made);
                        for (int i = 0; i < made; i++)
                            destination[i] = outputArray[i * _channels + ch];
                    }
                    framesDone += made;
                }

                // AudioBuffer starts cleared, so exhausted input is padded with silence.
                _stream.Complete(context, expectedOut);
                return output;
            }
            catch
            {
                output.Dispose();
                Reset();
                _stream.Invalidate();
                throw;
            }
            finally
            {
                ArrayPool<float>.Shared.Return(inputArray);
                ArrayPool<float>.Shared.Return(outputArray);
            }
        }
    }
}
