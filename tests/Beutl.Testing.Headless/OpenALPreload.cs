using System.Runtime.InteropServices;

namespace Beutl.Testing.Headless;

// Preload the bundled OpenAL library for Silk.NET on non-Windows test hosts.
public static partial class OpenALPreload
{
    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnvironmentVariable(string name, string value, int overwrite);

    public static void EnsureLoaded()
    {
        if (OperatingSystem.IsWindows())
        {
            // Playback uses XAudio2 on Windows.
            return;
        }

        SetEnvironmentVariable("ALSOFT_DRIVERS", "null", 1);

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["libopenal.dylib", "openal.dylib"]
            : ["libopenal.so", "libopenal.so.1", "openal.so"];

        foreach (string candidate in candidates)
        {
            // Keep the handle loaded for Silk.NET.
            if (NativeLibrary.TryLoad(candidate, typeof(OpenALPreload).Assembly, null, out _))
            {
                return;
            }
        }
    }
}
