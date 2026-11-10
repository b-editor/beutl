using System.Globalization;
using Beutl.Configuration;

namespace Beutl;

/// <summary>
/// Writes times as the editor shows them, in one of the <see cref="TimeDisplayFormat"/>s: the preview's current and
/// total times, the seek bar's tip, the timeline's ruler and the markers' times. <see cref="GotoTimecodeParser"/> reads
/// each of them back.
/// </summary>
public static class TimeDisplayFormatter
{
    /// <summary>Writes <paramref name="time"/> in <paramref name="format"/>.</summary>
    /// <param name="time">A time from the start of the timeline.</param>
    /// <param name="frameRate">The rate that timecodes and frame numbers count frames at; it must be positive.</param>
    /// <param name="format">How to write the time.</param>
    /// <returns>
    /// The time, such as <c>00:00:02.50</c>, <c>00:00:02:15</c> or <c>75</c> for 2.5 seconds at 30 fps. A time between two
    /// frames is written as the nearer frame, which is the one the preview shows there.
    /// </returns>
    /// <remarks>
    /// <see cref="TimeDisplayFormat.Time"/> wraps at 24 hours, as the editor has always written times, while a timecode
    /// counts its hours on. At 29.97 and 59.94 fps a timecode counts its frames in drop-frame, which keeps it within two
    /// frames of the clock, and a semicolon goes before them: <c>00:01:00;02</c>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameRate"/> is zero or negative.</exception>
    public static string Format(MediaTime time, Rational frameRate, TimeDisplayFormat format)
    {
        TimecodeCounting counting = TimecodeCounting.For(frameRate);
        return format switch
        {
            TimeDisplayFormat.Timecode => FormatTimecode(time.ToFrame(frameRate, MidpointRounding.AwayFromZero), counting),
            TimeDisplayFormat.Frames => time.ToFrame(frameRate, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),
            _ => time.ToTimeSpan().ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture),
        };
    }

    private static string FormatTimecode(long frame, TimecodeCounting counting)
    {
        // A frame before the start of the timeline counts back from it.
        ulong number = unchecked(frame < 0 ? 0UL - (ulong)frame : (ulong)frame);
        if (counting.Dropped > 0)
            number = counting.ToDropFrameNumber(number);

        ulong frames = number % counting.Base;
        ulong seconds = number / counting.Base;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(frame < 0 ? "-" : "")}{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}{(counting.Dropped > 0 ? ';' : ':')}{frames.ToString(counting.FramesFormat, CultureInfo.InvariantCulture)}");
    }
}

// How a timecode counts the frames of a frame rate: Base frames make a second, which is the rate rounded up, and in
// drop-frame the first Dropped numbers of every minute but each tenth are skipped, so that the numbers keep up with the
// clock.
internal readonly record struct TimecodeCounting(ulong Base, int Dropped)
{
    public static TimecodeCounting For(Rational frameRate)
    {
        Int128 numerator = frameRate.Numerator;
        Int128 denominator = frameRate.Denominator;
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        if (numerator <= 0 || denominator == 0)
            throw new ArgumentOutOfRangeException(nameof(frameRate), frameRate, "The frame rate must be positive.");

        // 29.97 fps (30000/1001) skips 2 numbers a minute and 59.94 fps (60000/1001) skips 4.
        int dropped = numerator * 1001 == denominator * 30000 ? 2
            : numerator * 1001 == denominator * 60000 ? 4
            : 0;
        return new TimecodeCounting((ulong)((numerator + denominator - 1) / denominator), dropped);
    }

    // As many digits as the frames of a second reach, and two at least.
    public string FramesFormat
    {
        get
        {
            int digits = 2;
            for (ulong limit = 100; limit < Base && digits < 20; limit *= 10)
                digits++;

            return "D" + digits.ToString(CultureInfo.InvariantCulture);
        }
    }

    private ulong FramesPerMinute => (Base * 60) - (ulong)Dropped;

    private ulong FramesPerTenMinutes => (Base * 600) - ((ulong)Dropped * 9);

    // The number that a drop-frame timecode gives the frame: the frame counted with the numbers that were skipped.
    public ulong ToDropFrameNumber(ulong frame)
    {
        ulong tens = frame / FramesPerTenMinutes;
        ulong rest = frame % FramesPerTenMinutes;
        ulong number = frame + ((ulong)Dropped * 9 * tens);
        // The first minute of every ten keeps its numbers.
        if (rest > (ulong)Dropped)
            number += (ulong)Dropped * ((rest - (ulong)Dropped) / FramesPerMinute);

        return number;
    }

    // The frame that a timecode names. A field past its range makes it invalid, and a frame past the range of a long out
    // of range. In drop-frame a number that is skipped names the frame after it, the first of its minute.
    public bool TryGetFrame(ulong hours, ulong minutes, ulong seconds, ulong frames, out long frame, out GotoTimecodeError error)
    {
        frame = 0;
        if (minutes >= 60 || seconds >= 60 || frames >= Base)
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        try
        {
            checked
            {
                ulong totalMinutes = (hours * 60) + minutes;
                ulong skipped = 0;
                if (Dropped > 0)
                {
                    if (seconds == 0 && frames < (ulong)Dropped && totalMinutes % 10 != 0)
                        frames = (ulong)Dropped;

                    skipped = (ulong)Dropped * (totalMinutes - (totalMinutes / 10));
                }

                frame = (long)((((totalMinutes * 60) + seconds) * Base) + frames - skipped);
            }
        }
        catch (OverflowException)
        {
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        error = GotoTimecodeError.None;
        return true;
    }
}
