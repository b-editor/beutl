using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// The frames a playback run renders: from <see cref="StartFrame"/>, the frame at the playhead, up to but not
/// including <see cref="EndFrame"/>, the frame at the end of the scene.
/// </summary>
/// <remarks>
/// Both are rounded to the nearest frame, as the paused preview rounds the playhead. Frame-aligned times are whole
/// ticks just short of the frame boundary at 24, 30 and 60 fps, so truncating them would start playback a frame
/// before the one the preview shows, behind the audio, which starts at the playhead itself. The end frame comes from
/// the end of the scene, not from its start and length rounded separately, which can miss the last frame.
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
