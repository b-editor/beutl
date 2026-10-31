using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.WebBrowserTab;

// GLib plumbing shared by the native WebKitGTK and WPE integrations.
internal static partial class LinuxWebKit
{
    internal static readonly string[] GtkLibraries = ["libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37"];
    internal static readonly string[] WpeLibraries = ["libWPEWebKit-2.0.so.1"];

    internal static nint LoadLibrary(string[] libraries)
    {
        foreach (string library in libraries)
            if (NativeLibrary.TryLoad(library, out nint handle)) return handle;
        throw new DllNotFoundException("WebKit is not available.");
    }

    internal static unsafe Task OnGlibAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = GCHandle.Alloc((action, completion));
        // g_main_context_invoke may run immediately on the calling thread. An idle source
        // always dispatches on the context used by Avalonia's GTK/WPE event integration.
        IdleAdd(0, (nint)(delegate* unmanaged[Cdecl]<nint, int>)&RunIdle, GCHandle.ToIntPtr(state), 0);
        return completion.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RunIdle(nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var (action, completion) = ((Action, TaskCompletionSource))handle.Target!;
        try { action(); completion.TrySetResult(); }
        catch (Exception ex) { completion.TrySetException(ex); }
        finally { handle.Free(); }
        return 0;
    }

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_ref")] internal static partial nint ObjectRef(nint value);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] internal static partial void ObjectUnref(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] internal static partial void ErrorFree(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_idle_add_full")] private static partial uint IdleAdd(int priority, nint callback, nint data, nint destroy);
}
