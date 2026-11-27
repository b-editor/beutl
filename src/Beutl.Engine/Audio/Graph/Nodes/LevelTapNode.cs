using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

// A pass-through that remembers what its branch played. A SoundGroup places one on each child branch so the
// composer can detect ducking even when the child is mixed by the group instead of being a top-level entry.
internal sealed class LevelTapNode : AudioNode
{
    private AudioBuffer? _peaks;
    private int _peakCount;

    // The row of the sound this tap reads; the composer maps it to the layer the sound belongs to.
    public required int ZIndex { get; init; }

    // The block the peaks belong to: Peaks[0] is at this time in the scene's timeline.
    public MediaTime BlockStart { get; private set; }

    public int BlockSampleRate { get; private set; }

    public ReadOnlySpan<float> Peaks
        => _peaks is null || _peakCount == 0 ? default : _peaks.GetChannelData(0)[.._peakCount];

    public override AudioBuffer Process(AudioProcessContext context)
        => PassThrough(Inputs[0].Process(context), context);

    public override AudioBuffer Flush(AudioProcessContext context)
        => PassThrough(Inputs[0].Flush(context), context);

    private AudioBuffer PassThrough(AudioBuffer input, AudioProcessContext context)
    {
        try
        {
            Record(input, context);
            return RecordProcessedOutput(input);
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    private void Record(AudioBuffer input, AudioProcessContext context)
    {
        int count = input.SampleCount;
        if (count == 0)
        {
            _peakCount = 0;
            BlockStart = context.TimeRange.Start;
            BlockSampleRate = context.SampleRate;
            return;
        }

        if (_peaks is null || _peaks.SampleRate != context.SampleRate || _peaks.SampleCount < count)
        {
            var replacement = new AudioBuffer(context.SampleRate, 1, count);
            _peaks?.Dispose();
            _peaks = replacement;
        }

        Span<float> peaks = _peaks!.GetChannelData(0)[..count];
        peaks.Clear();
        for (int channel = 0; channel < input.ChannelCount; channel++)
        {
            ReadOnlySpan<float> samples = input.GetChannelData(channel);
            for (int i = 0; i < count; i++)
            {
                float magnitude = MathF.Abs(samples[i]);
                if (float.IsFinite(magnitude) && magnitude > peaks[i])
                    peaks[i] = magnitude;
            }
        }

        _peakCount = count;
        BlockStart = context.TimeRange.Start;
        BlockSampleRate = context.SampleRate;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _peaks?.Dispose();
            _peaks = null;
        }

        base.Dispose(disposing);
    }
}
