namespace Beutl.Editor.Services;

public sealed record class AudioFrameSnapshot(
    float[] Interleaved,
    int SampleRate,
    int ChannelCount,
    TimeSpan StartTime)
{
    public int SampleCount => ChannelCount == 0 ? 0 : Interleaved.Length / ChannelCount;

    public TimeSpan Duration => SampleRate == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(SampleCount / (double)SampleRate);
}
