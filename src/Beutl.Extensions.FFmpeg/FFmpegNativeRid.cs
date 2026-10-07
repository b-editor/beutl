using System.Runtime.InteropServices;

namespace Beutl.Extensions.FFmpeg;

/// <summary>
/// Maps a process architecture to the folder names that probe paths for its FFmpeg shared libraries
/// are built from: the Windows runtime identifier in <c>runtimes/&lt;rid&gt;/native</c> for the
/// bundled libraries, and the Linux multiarch triplet in <c>/usr/lib/&lt;triplet&gt;</c> for the
/// system ones.
/// </summary>
/// <remarks>
/// <see cref="System.Environment.Is64BitProcess"/> cannot distinguish x64 from arm64 — both are
/// 64-bit — so an arm64 process would otherwise probe the <c>win-x64</c> or
/// <c>x86_64-linux-gnu</c> folder and fail to find or load the native libraries
/// (<see cref="System.BadImageFormatException"/>). Selecting on
/// <see cref="RuntimeInformation.ProcessArchitecture"/> keeps each architecture pointed at its
/// own native folder.
/// </remarks>
internal static class FFmpegNativeRid
{
    /// <summary>Returns the Windows RID folder name for the given architecture.</summary>
    public static string GetWindowsRid(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => "win-x64",
    };

    /// <summary>Returns the Windows RID folder name for the current process architecture.</summary>
    public static string GetWindowsRid() => GetWindowsRid(RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// Returns the Debian-style multiarch directory name for the given architecture, or
    /// <see langword="null"/> when there is none to probe.
    /// </summary>
    public static string? GetLinuxMultiarchDirectory(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x86_64-linux-gnu",
        Architecture.Arm64 => "aarch64-linux-gnu",
        Architecture.X86 => "i386-linux-gnu",
        Architecture.Arm => "arm-linux-gnueabihf",
        _ => null,
    };

    /// <summary>Returns the multiarch directory name for the current process architecture.</summary>
    public static string? GetLinuxMultiarchDirectory() => GetLinuxMultiarchDirectory(RuntimeInformation.ProcessArchitecture);
}
