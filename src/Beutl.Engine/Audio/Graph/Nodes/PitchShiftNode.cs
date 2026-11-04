using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

// Two reading points move through a delay line at the speed the pitch asks for. Each fades out before it reaches an
// end of the line and jumps to the other end, while the other reading point, half a line away, is at full level.
public sealed class PitchShiftNode : AudioNode
{
    // Long enough for low notes to keep their shape, short enough not to be heard as an echo.
    private const double WindowSeconds = 0.05;

    private DelayLines? _delayLines;
    private int _lastSampleRate;
    private double _position = 0.5;
    private float _lastSemitones;
    private MediaTime? _lastTimeRangeEnd;

    public required IProperty<float> Semitones { get; init; }

    internal static int GetWindowSamples(int sampleRate) => (int)(sampleRate * WindowSeconds);

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Pitch shift node requires exactly one input.");

        return RecordProcessedOutput(ProcessTail(Inputs[0].Process(context), context, draining: false));
    }

    public override int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return GetWindowSamples(sampleRate);
    }

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining)
    {
        using AudioBuffer owned = input;
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        try
        {
            int window = GetWindowSamples(context.SampleRate);
            if (_delayLines == null || _lastSampleRate != context.SampleRate || _delayLines.ChannelCount != input.ChannelCount)
            {
                _delayLines?.Dispose();
                _delayLines = new DelayLines(input.ChannelCount, window);
                _lastSampleRate = context.SampleRate;
                _lastTimeRangeEnd = null;
            }

            if (_lastTimeRangeEnd is not { } end || !context.ContinuesFrom(end))
            {
                _delayLines.Clear();
                // In the middle of the line one reading point is at full level, and with no shift it stays there.
                _position = 0.5;
            }

            _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

            Span<float> semitones = stackalloc float[Math.Min(input.SampleCount, 1024)];
            Span<float> positions = stackalloc float[semitones.Length];

            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = Math.Min(semitones.Length, input.SampleCount - processed);
                if (draining)
                {
                    semitones[..chunkSize].Fill(_lastSemitones);
                }
                else
                {
                    context.AnimationSampler.SampleBuffer(
                        Semitones, context.GetChunkRange(processed, chunkSize), context.SampleRate, semitones[..chunkSize]);
                    _lastSemitones = semitones[chunkSize - 1];
                }

                for (int i = 0; i < chunkSize; i++)
                {
                    positions[i] = (float)_position;
                    _position += (1 - Math.Pow(2, semitones[i] / 12)) / window;
                    _position -= Math.Floor(_position);
                }

                for (int ch = 0; ch < input.ChannelCount; ch++)
                {
                    ReadOnlySpan<float> inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    Span<float> outData = output.GetChannelData(ch).Slice(processed, chunkSize);
                    for (int i = 0; i < chunkSize; i++)
                    {
                        _delayLines.Write(ch, inData[i]);
                        double first = positions[i];
                        double second = first < 0.5 ? first + 0.5 : first - 0.5;
                        double level = Math.Sin(Math.PI * first);
                        level *= level;
                        outData[i] = (float)(_delayLines.Read(ch, first * window) * level
                                             + _delayLines.Read(ch, second * window) * (1 - level));
                    }
                }

                processed += chunkSize;
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _delayLines?.Dispose();

        base.Dispose(disposing);
    }
}
