using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.Interop
{
    /// <summary>
    /// Contains the native operating system methods.
    /// </summary>
    internal static class NativeMethods
    {
        private static string MacOSDefautDirectory => "/opt/local/lib/";
        private static string LinuxDefaultDirectory => $"/usr/lib/{(Environment.Is64BitOperatingSystem ? "x86_64" : "x86")}-linux-gnu";
        private static string WindowsDefaultDirectory => $@"\runtimes\{(Environment.Is64BitProcess ? "win-x64" : "win-x86")}\native";

        /// <summary>
        /// Gets the default FFmpeg directory for current platform.
        /// </summary>
        /// <returns>A path to the default directory for FFmpeg libraries.</returns>
        internal static string GetFFmpegDirectory()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Environment.CurrentDirectory + WindowsDefaultDirectory;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxDefaultDirectory;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacOSDefautDirectory;
            }
            else
            {
                throw new PlatformNotSupportedException("This OS is not supported by the BEditor.Media");
            }
        }
    }
}
