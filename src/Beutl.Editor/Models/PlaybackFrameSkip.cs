namespace Beutl.Models;

internal static class PlaybackFrameSkip
{
    /// <summary>
    /// Returns the frame the playback producer renders next after finishing
    /// <paramref name="producedFrame"/>, catching up to <paramref name="requestedFrame"/> when the
    /// consumer has moved past it.
    /// </summary>
    /// <remarks>
    /// Never overshoots <paramref name="requestedFrame"/>: the consumer displays whatever it is handed,
    /// so a later frame puts a future picture on screen and freezes there until the clock catches up.
    /// </remarks>
    public static int ResolveNextFrame(int producedFrame, int? requestedFrame)
    {
        int next = producedFrame + 1;
        return requestedFrame is { } requested && requested > next ? requested : next;
    }
}
