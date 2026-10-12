using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// The frames a playback run renders: from <see cref="StartFrame"/>, the frame at the playhead, up to but not
/// including <see cref="EndFrame"/>, the frame at the end of the scene.
/// </summary>
/// <remarks>
/// Both are rounded to the nearest frame: frame-aligned times fall a tick short of the boundary, so truncating them
/// would start playback a frame early. The end frame comes from the end of the scene.
/// </remarks>
internal readonly record struct PlaybackRange(int StartFrame, int EndFrame)
{
    public static PlaybackRange From(TimeSpan currentTime, TimeSpan sceneStart, TimeSpan sceneDuration, int rate)
    {
        // The frames from zero to a time, rounded to the nearest frame, number the frame at that time.
        var frameRate = new Rational(rate);
        return new PlaybackRange(
            (int)FrameProviderImpl.ToFrameCount(currentTime, frameRate),
            (int)FrameProviderImpl.ToFrameCount(sceneStart + sceneDuration, frameRate));
    }
}
