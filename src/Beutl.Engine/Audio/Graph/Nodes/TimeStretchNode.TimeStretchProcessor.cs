using System.Buffers;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class TimeStretchNode
{
    private sealed class TimeStretchProcessor : ISpeedMappedProcessor
    {
        private const int BLOCK = 256;
        private const int TimeStretchFeedFrames = 1024;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly TimeStretchNode _node;
        private readonly WsolaFeed _feed;
        private readonly AudioSourceStream _stream;
        private readonly WsolaTimeStretcher _stretcher;
        private float? _staticSpeed;

        public TimeStretchProcessor(int sampleRate, int channels, TimeStretchNode node)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _node = node;
            _feed = new WsolaFeed(sampleRate, channels, node._mapping.MapOutputTimeToSource);
            _stream = _feed.Stream;
            _stretcher = _feed.Stretcher;
        }

        public bool CanPassThroughDrain => _stream.CanPassThroughDrain;

        public void TrackPassthrough(AudioProcessContext context, int sampleCount)
        {
            _feed.Reset();
            _stream.TrackPassthrough(context, sampleCount);
            _staticSpeed = 1f;
        }

        private void BeginStream(double outputStartSeconds, double sourceStartSeconds, bool forceReanchor)
        {
            bool seek = _stream.Begin(outputStartSeconds, sourceStartSeconds, forceReanchor);
            if (seek)
                _feed.Reset();
        }

        public AudioBuffer ProcessBuffer(AudioProcessContext context, float speed, int expectedOut, bool draining, bool forceReanchor)
        {
            double outputStart = context.TimeRange.Start.ToSeconds();
            bool configurationChanged = _staticSpeed is null || Math.Abs(_staticSpeed.Value - speed) > 1e-4f;
            BeginStream(outputStart, outputStart * speed,
                forceReanchor: _stream.IsInitialized && !draining && (configurationChanged || forceReanchor));
            _staticSpeed = speed;
            return ProcessTimeStretch(context, default, speed, expectedOut, draining);
        }

        public AudioBuffer ProcessBufferWithVariableSpeed(AudioProcessContext context, ReadOnlySpan<double> speeds,
            int expectedOut, double sourceStartSeconds, bool draining, bool forceReanchor)
        {
            BeginStream(context.TimeRange.Start.ToSeconds(), sourceStartSeconds,
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
            if (draining && !_feed.IsDraining)
            {
                // Analysis lookahead belongs to live processing, not to the held upstream tail.
                // The live boundary kept the upstream state at its terminal sample; start a fresh
                // drain stream there and consume only the latency that the upstream still retains.
                _feed.BeginDrain(_node);
            }
            long? inputEnd = draining ? _feed.InputEnd : _stream.GetInputEnd(_node.Inputs[0], context);
            if (!draining && _feed.InvalidatesReadAhead(inputEnd))
            {
                // Differential updates can change a downstream clip without replacing this processor.
                // Re-anchor at the playback position to discard post-trim lookahead or reopen a
                // finished stream, since lookahead or draining may have moved the read cursor ahead.
                BeginStream(context.TimeRange.Start.ToSeconds(),
                    _node._mapping.MapOutputTimeToSource(context.TimeRange.Start).ToSeconds(),
                    forceReanchor: true);
            }
            _feed.InputEnd = inputEnd;
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
                        if (_feed.IsFlushed)
                            break;

                        _feed.Feed(input, _node.Inputs[0], context, draining);
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
                _feed.Reset();
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
