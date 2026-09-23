using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class LinuxAdBlockBackend : IBrowserAdBlockBackend
{
    private readonly Api _api;
    private nint _manager;
    private nint _filter;
    private bool _enabled;
    private LinuxAdBlockBackend(Api api, nint manager, nint filter) => (_api, _manager, _filter) = (api, manager, filter);

    internal static async Task<IBrowserAdBlockBackend> CreateAsync(nint webView, bool wpe, BrowserAdBlockRules rules)
    {
        Api api = wpe ? Api.Wpe.Value : Api.Gtk.Value;
        // Retain before dispatch; release on GLib too because the last unref can destroy a widget.
        ObjectRef(webView);
        try
        {
            string json = await Task.Run(rules.ToWebKitJson);
            string path = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "compiled-ad-filters");
            Directory.CreateDirectory(path);
            var completion = new TaskCompletionSource<IBrowserAdBlockBackend>(TaskCreationOptions.RunContinuationsAsynchronously);
            await OnGlibAsync(() => BeginCompile(api, webView, path, json, completion));
            return await completion.Task;
        }
        finally { await OnGlibAsync(() => ObjectUnref(webView)); }
    }

    public Task EnableAsync() => OnGlibAsync(() =>
    {
        if (_manager == 0 || _enabled) return;
        _api.AddFilter(_manager, _filter);
        _enabled = true;
    });

    public void Dispose()
    {
        nint manager = _manager, filter = _filter;
        _manager = _filter = 0;
        if (manager == 0) return;
        _ = OnGlibAsync(() =>
        {
            if (_enabled) _api.RemoveFilter(manager, filter);
            _api.UnrefFilter(filter);
            ObjectUnref(manager);
        });
    }

    private static unsafe void BeginCompile(Api api, nint webView, string path, string json, TaskCompletionSource<IBrowserAdBlockBackend> completion)
    {
        nint manager = ObjectRef(api.GetManager(webView));
        nint store = api.CreateStore(path);
        nint bytes = 0;
        var state = GCHandle.Alloc(new Compilation(api, manager, store, "beutl-" + Guid.NewGuid().ToString("N"), completion));
        try
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(json);
            fixed (byte* source = utf8) bytes = BytesNew((nint)source, (nuint)utf8.Length);
            var compilation = (Compilation)state.Target!;
            api.Save(store, compilation.Identifier, bytes, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnCompiled, GCHandle.ToIntPtr(state));
        }
        catch
        {
            state.Free();
            ObjectUnref(manager);
            ObjectUnref(store);
            throw;
        }
        finally { if (bytes != 0) BytesUnref(bytes); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCompiled(nint store, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var state = (Compilation)handle.Target!;
        try
        {
            nint filter = state.Api.Finish(store, result, out nint error);
            if (filter == 0)
            {
                if (error != 0) ErrorFree(error);
                ObjectUnref(state.Manager);
                state.Completion.TrySetException(new InvalidDataException("WebKit could not compile the filter list."));
            }
            else
            {
                state.Api.RemoveStored(store, state.Identifier);
                state.Completion.TrySetResult(new LinuxAdBlockBackend(state.Api, state.Manager, filter));
            }
        }
        catch (Exception ex) { state.Completion.TrySetException(ex); }
        finally { ObjectUnref(state.Store); handle.Free(); }
    }

    private sealed record Compilation(Api Api, nint Manager, nint Store, string Identifier, TaskCompletionSource<IBrowserAdBlockBackend> Completion);

    private static unsafe Task OnGlibAsync(Action action)
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

    private sealed unsafe class Api
    {
        internal static readonly Lazy<Api> Gtk = new(() => new(["libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37"]));
        internal static readonly Lazy<Api> Wpe = new(() => new(["libWPEWebKit-2.0.so.1"]));
        private readonly nint _library;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _getManager;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _addFilter, _removeFilter;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _unrefFilter;
        private readonly delegate* unmanaged[Cdecl]<nint, nint> _newStore;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, void> _save;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint*, nint> _finish;
        private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void> _removeStored;

        private Api(string[] libraries)
        {
            foreach (string library in libraries)
                if (NativeLibrary.TryLoad(library, out _library)) break;
            if (_library == 0) throw new DllNotFoundException("WebKit is not available.");
            _getManager = (delegate* unmanaged[Cdecl]<nint, nint>)Export("webkit_web_view_get_user_content_manager");
            _addFilter = (delegate* unmanaged[Cdecl]<nint, nint, void>)Export("webkit_user_content_manager_add_filter");
            _removeFilter = (delegate* unmanaged[Cdecl]<nint, nint, void>)Export("webkit_user_content_manager_remove_filter");
            _unrefFilter = (delegate* unmanaged[Cdecl]<nint, void>)Export("webkit_user_content_filter_unref");
            _newStore = (delegate* unmanaged[Cdecl]<nint, nint>)Export("webkit_user_content_filter_store_new");
            _save = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, void>)Export("webkit_user_content_filter_store_save");
            _finish = (delegate* unmanaged[Cdecl]<nint, nint, nint*, nint>)Export("webkit_user_content_filter_store_save_finish");
            _removeStored = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)Export("webkit_user_content_filter_store_remove");
        }

        private nint Export(string name) => NativeLibrary.GetExport(_library, name);
        internal nint GetManager(nint view) => _getManager(view);
        internal void AddFilter(nint manager, nint filter) => _addFilter(manager, filter);
        internal void RemoveFilter(nint manager, nint filter) => _removeFilter(manager, filter);
        internal void UnrefFilter(nint filter) => _unrefFilter(filter);
        internal nint CreateStore(string path)
        {
            nint text = Marshal.StringToCoTaskMemUTF8(path);
            try { return _newStore(text); }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        internal void Save(nint store, string id, nint bytes, nint callback, nint data)
        {
            nint text = Marshal.StringToCoTaskMemUTF8(id);
            try { _save(store, text, bytes, 0, callback, data); }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        internal nint Finish(nint store, nint result, out nint error)
        {
            nint nativeError = 0;
            nint filter = _finish(store, result, &nativeError);
            error = nativeError;
            return filter;
        }
        internal void RemoveStored(nint store, string id)
        {
            nint text = Marshal.StringToCoTaskMemUTF8(id);
            try { _removeStored(store, text, 0, 0, 0); }
            finally { Marshal.FreeCoTaskMem(text); }
        }
    }

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_ref")] private static partial nint ObjectRef(nint value);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] private static partial void ObjectUnref(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_bytes_new")] private static partial nint BytesNew(nint data, nuint size);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_bytes_unref")] private static partial void BytesUnref(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] private static partial void ErrorFree(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_idle_add_full")] private static partial uint IdleAdd(int priority, nint callback, nint data, nint destroy);
}
