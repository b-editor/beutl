using System.Runtime.InteropServices;

namespace Beutl.Extensions.FFmpeg;

/// <summary>
/// Maps a process architecture to the Windows native runtime folder name
/// (<c>runtimes/&lt;rid&gt;/native</c>) used to locate the bundled FFmpeg shared libraries.
/// </summary>
/// <remarks>
/// <see cref="System.Environment.Is64BitProcess"/> cannot distinguish x64 from arm64 — both are
/// 64-bit — so an arm64 process would otherwise probe the <c>win-x64</c> folder and fail to load
/// the native libraries (<see cref="System.BadImageFormatException"/>). Selecting on
/// <see cref="RuntimeInformation.ProcessArchitecture"/> keeps each architecture pointed at its
/// own native folder.
/// </remarks>
public static class FFmpegNativeRid
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
}
