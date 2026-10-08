using System.Buffers;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class SpeedNode
{
    private sealed partial class SpeedProcessor
    {
        private const int TimeStretchFeedFrames = 1024;
        private readonly WsolaTimeStretcher? _timeStretch;
        private bool _timeStretchFlushed;

        private static WsolaTimeStretcher CreateTimeStretchProcessor(int sampleRate, int channels)
            => new(sampleRate, channels);

        private void ResetTimeStretch()
        {
            _timeStretch?.Clear();
            _timeStretchFlushed = false;
        }

        // Pull enough continuous source audio to satisfy the requested output. The stretcher's initial
        // buffering is lookahead here, so it adds no leading silence or output-domain latency.
        // Keep the FIFO across calls: flushing it at every timeline chunk would create audible seams.
        private AudioBuffer ProcessTimeStretch(
            AudioProcessContext context,
            ReadOnlySpan<double> speedCurve,
            float speed,
            int expectedOut,
            bool draining)
        {
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
                    _timeStretch!.Tempo = speed;
                    int made = _timeStretch.ReceiveSamples(
                        outputArray.AsSpan(0, framesThis * _channels), framesThis, curve);
                    if (made == 0)
                    {
                        if (_timeStretchFlushed)
                            break;

                        int got = Read(input, context, draining) / _channels;
                        if (got > 0)
                        {
                            _timeStretch.PutSamples(input[..(got * _channels)], got);
                        }
                        else
                        {
                            // A short/exhausted source must release its final processing window once.
                            _timeStretch.Flush();
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
                _nextOutputStart = context.TimeRange.Start.TotalSeconds + (double)expectedOut / _sampleRate;
                _sourceCursorMatchesOutputTimeline = false;
                return output;
            }
            catch
            {
                output.Dispose();
                ResetTimeStretch();
                _initialized = false;
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
