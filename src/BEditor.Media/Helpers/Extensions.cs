using System;
using System.ComponentModel;

using BEditor.Media.Common;

using FFmpeg.AutoGen;

namespace BEditor.Media.Helpers
{
    /// <summary>
    /// Contains extension methods.
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// Gets the <see cref="DescriptionAttribute"/> value of the specified enumeration value.
        /// </summary>
        /// <param name="value">The enum value.</param>
        /// <returns>The description attribute string of this enum value.</returns>
        public static string GetDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString())!;
            return Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute
                ? attribute.Description : value.ToString();
        }

        /// <summary>
        /// Checks if this object is equal to at least one of specified objects.
        /// </summary>
        /// <typeparam name="T">Type of the objects.</typeparam>
        /// <param name="value">This object.</param>
        /// <param name="valueToCompare">Objects to check.</param>
        /// <returns><see langword="true"/> is the object is equal to at least one of specified objects.</returns>
        public static bool IsMatch<T>(this T value, params T[] valueToCompare)
            where T : struct, Enum
        {
            foreach (var x in valueToCompare)
            {
                if (value.Equals(x))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Normalizes this enumeration value - makes it lowercase and trims the specified amount of chars.
        /// </summary>
        /// <param name="value">The enumeration value to format.</param>
        /// <param name="charsToTrim">Number of chars to trim.</param>
        /// <returns>The normalized string.</returns>
        internal static string FormatEnum(this Enum value, int charsToTrim)
        {
            return value.ToString()[charsToTrim..].ToLower();
        }

        /// <summary>
        /// Gets the type of content in the <see cref="AVFrame"/>.
        /// </summary>
        /// <param name="frame">The <see cref="AVFrame"/>.</param>
        /// <returns>The type of frame content.</returns>
        internal static MediaType GetMediaType(this AVFrame frame)
        {
            if (frame.width > 0 && frame.height > 0)
            {
                return MediaType.Video;
            }

            if (frame.channels > 0)
            {
                return MediaType.Audio;
            }

            return MediaType.None;
        }
    }
}