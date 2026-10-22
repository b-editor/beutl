namespace Beutl.Editor.Models;

public sealed record class AudioFrameSnapshot(
    float[] Interleaved,
    int SampleRate,
    int ChannelCount,
    MediaTime StartTime)
{
    public int SampleCount => ChannelCount == 0 ? 0 : Interleaved.Length / ChannelCount;
}
