using Beutl.Media;

namespace Beutl.Audio.Graph;

// Observe the synchronous graph pull, including nested composers and time-mapped reads.
// Playback keeps its silent fallback; analysis must not mistake that fallback for decoded audio.
internal sealed class AudioReadScope : IDisposable
{
    [ThreadStatic] private static AudioReadScope? s_current;
    private readonly AudioReadScope? _previous = s_current;

    public AudioReadScope() => s_current = this;

    public bool HasFailures { get; private set; }

    public static void ReportFailure()
    {
        if (s_current is { } scope) scope.HasFailures = true;
    }

    // Keep missing sources behind their clip node so an inactive child in a SoundGroup cannot
    // reject an otherwise readable window. No extra graph is needed for ordinary playback.
    public static void AddUnavailableSource(AudioContext context, TimeRange range)
    {
        if (s_current == null) return;
        var source = context.AddNode(new UnavailableSourceNode());
        var clip = context.CreateClipNode(range.Start, range.Duration);
        context.Connect(source, clip);
        context.MarkAsOutput(clip);
    }

    public void Dispose()
    {
        s_current = _previous;
        if (HasFailures) ReportFailure();
    }

    private sealed class UnavailableSourceNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            ReportFailure();
            return new AudioBuffer(context.SampleRate, LastProcessedChannelCount, context.GetSampleCount());
        }
    }
}
