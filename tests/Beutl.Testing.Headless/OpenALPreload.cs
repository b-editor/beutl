using System.Runtime.InteropServices;

namespace Beutl.Testing.Headless;

// Silk.NET loads OpenAL with a plain dlopen that only probes the system paths, the app base
// directory, and the main module directory — it cannot see the NuGet runtime-pack layout
// (runtimes/<rid>/native) that the bundled Silk.NET.OpenAL.Soft.Native ships in. Without a
// system-installed libopenal, audio playback therefore fails in test/dev environments even
// though the native library sits right next to the test binaries. Preload it through
// NativeLibrary (which performs deps.json probing and finds the runtime-pack layout) so
// Silk.NET's dlopen reuses the already-loaded handle.
public static class OpenALPreload
{
    public static void EnsureLoaded()
    {
        if (OperatingSystem.IsWindows())
        {
            // Playback uses XAudio2 on Windows.
            return;
        }

        // Headless environments (CI runners, machines without an audio server) have no audio
        // device, so OpenAL Soft's default backend probing fails with InvalidDevice the moment
        // the player opens a context. Route it to the software null backend instead: playback
        // position still advances without hardware, which is all the tests need.
        Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["libopenal.dylib", "openal.dylib"]
            : ["libopenal.so", "libopenal.so.1", "openal.so"];

        foreach (string candidate in candidates)
        {
            // Keep the handle loaded for the lifetime of the process: Silk.NET resolves it by
            // name later, and freeing it here would undo the preload.
            if (NativeLibrary.TryLoad(candidate, typeof(OpenALPreload).Assembly, null, out _))
            {
                return;
            }
        }
    }
}
