using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Avalonia.Platform;

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

    // WebKitGTK 4.1 and WPE 2.0 link libsoup 3, WebKitGTK 4.0 libsoup 2.4. Loading the other libsoup aborts the process.
    internal static (nint WebKit, nint Soup) LoadWithSoup(bool wpe)
    {
        if (wpe) return (LoadLibrary(WpeLibraries), NativeLibrary.Load("libsoup-3.0.so.0"));
        return NativeLibrary.TryLoad("libwebkit2gtk-4.1.so.0", out nint webkit)
            ? (webkit, NativeLibrary.Load("libsoup-3.0.so.0"))
            : (LoadLibrary(GtkLibraries), NativeLibrary.Load("libsoup-2.4.so.1"));
    }

    // The WebKitWebView behind a WebKitGTK or WPE adapter.
    internal static bool TryGetWebView(IPlatformHandle? handle, out nint webView, out bool wpe)
    {
        switch (handle)
        {
            case IGtkWebViewPlatformHandle gtk when OperatingSystem.IsLinux():
                webView = gtk.WebKitWebView;
                wpe = false;
                return true;
            case ILinuxWpePlatformHandle linuxWpe when OperatingSystem.IsLinux():
                webView = linuxWpe.WebKitWebView;
                wpe = true;
                return true;
            default:
                webView = 0;
                wpe = false;
                return false;
        }
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

    // Retains the object before dispatch and releases it on GLib too, because the last unref can destroy a widget.
    internal static Task OnGlibAsync(nint retained, Action action)
    {
        ObjectRef(retained);
        return OnGlibAsync(() =>
        {
            try { action(); }
            finally { ObjectUnref(retained); }
        });
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
