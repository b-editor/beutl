namespace Beutl.Editor.Models;

public sealed record class AudioFrameSnapshot(
    float[] Interleaved,
    int SampleRate,
    int ChannelCount,
    MediaTime StartTime)
{
    // Distinguishes repeated timestamps after a seek or loop in remote previews.
    internal long PlaybackId { get; init; }

    public int SampleCount => ChannelCount == 0 ? 0 : Interleaved.Length / ChannelCount;
}
