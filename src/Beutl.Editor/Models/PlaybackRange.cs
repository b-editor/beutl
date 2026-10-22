using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// The frames a playback run renders: from <see cref="StartFrame"/>, the frame at the playhead, up to but not
/// including <see cref="EndFrame"/>, the frame at the end of the scene.
/// </summary>
/// <remarks>
/// Both are rounded to the nearest frame: times read from a file fall just short of the boundary, so truncating them
/// would start playback a frame early. The end frame comes from the end of the scene.
/// </remarks>
internal readonly record struct PlaybackRange(int StartFrame, int EndFrame)
{
    public static PlaybackRange From(MediaTime currentTime, MediaTime sceneStart, MediaTime sceneDuration, Rational rate)
    {
        // The frames from zero to a time, rounded to the nearest frame, number the frame at that time.
        return new PlaybackRange(
            (int)FrameProviderImpl.ToFrameCount(currentTime, rate),
            (int)FrameProviderImpl.ToFrameCount(sceneStart + sceneDuration, rate));
    }
}
