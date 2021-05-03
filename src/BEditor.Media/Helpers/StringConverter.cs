using System;
using System.Runtime.InteropServices;
using System.Text;

using FFmpeg.AutoGen;

namespace BEditor.Media.Helpers
{
    /// <summary>
    /// Contains string conversion methods.
    /// </summary>
    internal static class StringConverter
    {
        /// <summary>
        /// Creates a new <see cref="string"/> from a pointer to the unmanaged UTF-8 string.
        /// </summary>
        /// <param name="pointer">A pointer to the unmanaged string.</param>
        /// <returns>The converted string.</returns>
        public static string Utf8ToString(this IntPtr pointer)
        {
            var lenght = 0;

            while (Marshal.ReadByte(pointer, lenght) != 0)
            {
                ++lenght;
            }

            var buffer = new byte[lenght];
            Marshal.Copy(pointer, buffer, 0, lenght);

            return System.Text.Encoding.UTF8.GetString(buffer);
        }

        /// <summary>
        /// Gets the FFmpeg error message based on the error code.
        /// </summary>
        /// <param name="errorCode">The error code.</param>
        /// <returns>The decoded error message.</returns>
        public static unsafe string DecodeMessage(int errorCode)
        {
            const int bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(errorCode, buffer, bufferSize);

            return new IntPtr(buffer).Utf8ToString();
        }
    }
}