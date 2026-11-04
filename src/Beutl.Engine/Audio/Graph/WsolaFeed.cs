namespace Beutl.Audio.Graph;

// The input side of a WSOLA stretch that reads ahead of its output: the source cursor, the stretcher it fills, and
// where the input stops. A drain starts a fresh stream at the cursor and takes only what the upstream still holds.
internal sealed class WsolaFeed(int sampleRate, int channels, Func<MediaTime, MediaTime> mapTime)
{
    public WsolaTimeStretcher Stretcher { get; } = new(sampleRate, channels);

    public AudioSourceStream Stream { get; } = new(sampleRate, channels, mapTime);

    public bool IsFlushed { get; private set; }

    public bool IsDraining { get; private set; }

    public long? InputEnd { get; set; }

    public void Reset()
    {
        Stretcher.Clear();
        IsFlushed = false;
        IsDraining = false;
        InputEnd = null;
    }

    public void BeginDrain(AudioNode owner)
    {
        Stretcher.Clear();
        IsFlushed = false;
        IsDraining = true;
        int latency = owner.GetMaxInputLatency(sampleRate, drain: true);
        InputEnd = latency == int.MaxValue ? null : checked(Stream.Position + latency);
    }

    // An edit can move the end without replacing the node: input read past a nearer end must go, and a stretcher
    // that has already released its last window cannot take more.
    public bool InvalidatesReadAhead(long? inputEnd)
    {
        bool endShrank = inputEnd is { } end
            && (InputEnd is { } previous ? end < previous : Stream.Position > end);
        bool reopened = IsFlushed && InputEnd is { } old && (inputEnd is null || inputEnd > old);
        return IsDraining || endShrank || reopened;
    }

    public void Feed(Span<float> buffer, AudioNode input, AudioProcessContext context, bool draining)
    {
        int got = Stream.Read(buffer, input, context, draining, InputEnd) / channels;
        if (got > 0)
            Stretcher.PutSamples(buffer[..(got * channels)], got);

        // A short or exhausted source must release its final processing window once.
        if (got == 0 || (InputEnd is { } end && Stream.Position >= end))
        {
            Stretcher.Flush();
            IsFlushed = true;
        }
    }
}
