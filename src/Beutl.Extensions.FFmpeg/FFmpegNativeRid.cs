using System.Runtime.InteropServices;

namespace Beutl.Extensions.FFmpeg;

/// <summary>
/// Maps a process architecture to the folders that hold its FFmpeg shared libraries: the Windows
/// native runtime folder (<c>runtimes/&lt;rid&gt;/native</c>) of the bundled libraries, and the
/// Linux multiarch directory (<c>/usr/lib/&lt;triplet&gt;</c>) of the system ones.
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
        _ => null,
    };

    /// <summary>Returns the multiarch directory name for the current process architecture.</summary>
    public static string? GetLinuxMultiarchDirectory() => GetLinuxMultiarchDirectory(RuntimeInformation.ProcessArchitecture);
}
