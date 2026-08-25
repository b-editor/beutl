using System.Runtime.InteropServices;

namespace Beutl.Testing.Headless;

// Preload the bundled OpenAL library for Silk.NET on non-Windows test hosts.
public static class OpenALPreload
{
    public static void EnsureLoaded()
    {
        if (OperatingSystem.IsWindows())
        {
            // Playback uses XAudio2 on Windows.
            return;
        }

        // Use the null backend when no audio device is available.
        Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");

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
