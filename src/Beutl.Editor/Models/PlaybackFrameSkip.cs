namespace Beutl.Models;

public static class PlaybackFrameSkip
{
    /// <summary>
    /// Returns the frame the playback producer renders next after finishing
    /// <paramref name="producedFrame"/>, catching up to <paramref name="requestedFrame"/> when the
    /// consumer has moved past it.
    /// </summary>
    /// <remarks>
    /// The result never overshoots <paramref name="requestedFrame"/>. The consumer displays whatever
    /// the producer hands it, so a later frame would put a future frame on screen and then freeze the
    /// preview until the playback clock caught up with it.
    /// </remarks>
    public static int ResolveNextFrame(int producedFrame, int? requestedFrame)
    {
        int next = producedFrame + 1;
        return requestedFrame is { } requested && requested > next ? requested : next;
    }
}
