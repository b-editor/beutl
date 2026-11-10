namespace Beutl.Configuration;

// How the editor writes a time: the preview's current and total times, the seek bar's tip, the timeline's ruler and the
// markers' times. The numbers are what settings files hold.
public enum TimeDisplayFormat
{
    // Hours, minutes, seconds and hundredths of a second, such as 00:00:02.50.
    Time = 0,

    // Hours, minutes, seconds and frames, such as 00:00:02:15. At 29.97 and 59.94 fps the frames are counted in
    // drop-frame, and a semicolon goes before them: 00:01:00;02.
    Timecode = 1,

    // The number of the frame from the start of the timeline, such as 75.
    Frames = 2,
}
