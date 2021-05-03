using System;

using FFmpeg.AutoGen;

namespace BEditor.Media.Helpers
{
    /// <summary>
    /// Contains extension methods for math types.
    /// </summary>
    internal static class MathHelper
    {
        /// <summary>
        /// Converts a rational number to a double.
        /// </summary>
        /// <param name="rational">The <see cref="AVRational"/> to convert.</param>
        /// <returns>The <see cref="double"/> value.</returns>
        public static double ToDouble(this AVRational rational)
        {
            return rational.den == 0 ? 0 : Convert.ToDouble(rational.num) / Convert.ToDouble(rational.den);
        }

        /// <summary>
        /// Converts the given <paramref name="timestamp"/> in the <paramref name="timeBase"/> units to a <see cref="TimeSpan"/> object.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="timeBase">The time base unit.</param>
        /// <returns>The converted <see cref="TimeSpan"/>.</returns>
        public static TimeSpan ToTimeSpan(this long timestamp, AVRational timeBase)
        {
            var ts = Convert.ToDouble(timestamp);
            var tb = timeBase.ToDouble();

            return TimeSpan.FromMilliseconds(ts * tb * 1000);
        }

        /// <summary>
        /// Converts the frame number to a <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="frameNumber">The frame number.</param>
        /// <param name="fps">The stream frame rate.</param>
        /// <returns>The converted <see cref="TimeSpan"/>.</returns>
        public static TimeSpan ToTimeSpan(this int frameNumber, double fps)
        {
            return TimeSpan.FromMilliseconds(frameNumber * (1000 / fps));
        }

        /// <summary>
        /// Converts this <see cref="TimeSpan"/> to a frame number based on the specified frame rate/>.
        /// </summary>
        /// <param name="time">The time.</param>
        /// <param name="framerate">The stream frame rate.</param>
        /// <returns>The frame number.</returns>
        public static int ToFrameNumber(this TimeSpan time, AVRational framerate)
        {
            return (int)(time.TotalSeconds * framerate.num / framerate.den);
        }

        /// <summary>
        /// Converts a frame index to a timestamp in the <paramref name="timeBase"/> units.
        /// </summary>
        /// <param name="frameNumber">The frame number.</param>
        /// <param name="fps">The stream frame rate.</param>
        /// <param name="timeBase">The stream time base.</param>
        /// <returns>The timestamp.</returns>
        public static long ToTimestamp(this int frameNumber, AVRational fps, AVRational timeBase)
        {
            long num = frameNumber * fps.den * timeBase.den;
            long den = fps.num * timeBase.num;
            return Convert.ToInt64(num / (double)den);
        }

        /// <summary>
        /// Converts the <see cref="TimeSpan"/> to a timestamp in the <paramref name="timeBase"/> units.
        /// </summary>
        /// <param name="time">The time.</param>
        /// <param name="timeBase">The stream time base.</param>
        /// <returns>The timestamp.</returns>
        public static long ToTimestamp(this TimeSpan time, AVRational timeBase)
        {
            return Convert.ToInt64(time.TotalSeconds * timeBase.den / timeBase.num);
        }

        /// <summary>
        /// Clamps the specified number between min and max values.
        /// </summary>
        /// <param name="number">The value to clamp.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>The clamped value.</returns>
        public static int Clamp(this int number, int min, int max)
        {
            if (number < min)
            {
                return min;
            }

            if (number > max)
            {
                return max;
            }

            return number;
        }
    }
}