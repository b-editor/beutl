using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class MacOSAdBlockBackend(nint manager, nint rules) : IBrowserAdBlockBackend
{
    private static readonly ConditionalWeakTable<BrowserAdBlockRules, Lazy<Task<CompiledRules>>> Compilations = new();
    private nint _manager = manager;
    private nint _rules = rules;
    private bool _enabled;
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    static MacOSAdBlockBackend() { }

    internal static async Task<IBrowserAdBlockBackend> CreateAsync(IAppleWKWebViewPlatformHandle handle, BrowserAdBlockRules rules)
    {
        nint configuration = Send(handle.WKWebView, Selector("configuration"));
        nint manager = Send(Send(configuration, Selector("userContentController")), Selector("retain"));
        try
        {
            CompiledRules compiled = await Compilations.GetValue(rules, key => new(() => CompileRulesAsync(key))).Value;
            nint retained = Send(compiled.Handle, Selector("retain"));
            GC.KeepAlive(compiled);
            return new MacOSAdBlockBackend(manager, retained);
        }
        catch { Compilations.Remove(rules); Send(manager, Selector("release")); throw; }
    }

    private static async Task<CompiledRules> CompileRulesAsync(BrowserAdBlockRules rules)
    {
        string json = await Task.Run(rules.ToWebKitJson);
        return new CompiledRules(await CompileAsync(json));
    }

    private sealed class CompiledRules(nint handle)
    {
        internal nint Handle { get; } = handle;
        ~CompiledRules()
        {
            nint rules = Handle;
            try { Avalonia.Threading.Dispatcher.UIThread.Post(() => Send(rules, Selector("release"))); }
            catch (InvalidOperationException) { /* The application dispatcher has already stopped. */ }
        }
    }

    public Task EnableAsync()
    {
        if (_manager != 0 && !_enabled)
        {
            Send(_manager, Selector("addContentRuleList:"), _rules);
            _enabled = true;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_manager == 0) return;
        if (_enabled) Send(_manager, Selector("removeContentRuleList:"), _rules);
        Send(_rules, Selector("release"));
        Send(_manager, Selector("release"));
        _rules = _manager = 0;
    }

    private static unsafe Task<nint> CompileAsync(string json)
    {
        var completion = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = GCHandle.Alloc(completion);
        // A copied Objective-C block retains its captured GCHandle until the one-shot completion.
        nint copied = CreateBlock((nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnCompiled, GCHandle.ToIntPtr(state));
        nint identifier = CreateString("beutl-adblock-" + Guid.NewGuid().ToString("N"));
        nint source = CreateString(json);
        try
        {
            nint store = Send(GetClass("WKContentRuleListStore"), Selector("defaultStore"));
            Send(store, Selector("compileContentRuleListForIdentifier:encodedContentRuleList:completionHandler:"), identifier, source, copied);
        }
        catch { state.Free(); throw; }
        finally
        {
            BlockRelease(copied);
            Send(source, Selector("release"));
            Send(identifier, Selector("release"));
        }
        return completion.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCompiled(nint block, nint ruleList, nint error)
    {
        GCHandle state = GCHandle.FromIntPtr(((Block*)block)->State);
        var completion = (TaskCompletionSource<nint>)state.Target!;
        try
        {
            if (ruleList != 0)
            {
                // The compiled file is temporary. The retained list remains valid after removal.
                nint identifier = Send(ruleList, Selector("identifier"));
                nint store = Send(GetClass("WKContentRuleListStore"), Selector("defaultStore"));
                // WebKit requires a non-null completion block even when its result is unused.
                nint removed = CreateBlock((nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnRemoved, 0);
                try { Send(store, Selector("removeContentRuleListForIdentifier:completionHandler:"), identifier, removed); }
                finally { BlockRelease(removed); }
                completion.TrySetResult(Send(ruleList, Selector("retain")));
            }
            else
            {
                nint description = error == 0 ? 0 : Send(error, Selector("localizedDescription"));
                string message = description == 0 ? "WebKit could not compile the filter list."
                    : Marshal.PtrToStringUTF8(Send(description, Selector("UTF8String"))) ?? "Invalid filter list.";
                completion.TrySetException(new InvalidDataException(message));
            }
        }
        catch (Exception ex) { completion.TrySetException(ex); }
        finally { state.Free(); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRemoved(nint block, nint error) { }

    private static nint CreateBlock(nint callback, nint state)
    {
        var block = new Block
        {
            Isa = StackBlockClass,
            Invoke = callback,
            Descriptor = BlockDescriptor,
            State = state
        };
        return BlockCopy(ref block);
    }

    private static readonly nint StackBlockClass = NativeLibrary.GetExport(NativeLibrary.Load(SystemLibrary), "_NSConcreteStackBlock");
    private static unsafe readonly nint BlockDescriptor = CreateDescriptor();
    private static unsafe nint CreateDescriptor()
    {
        nint* descriptor = (nint*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(nint)));
        descriptor[1] = sizeof(Block);
        return (nint)descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Block
    {
        internal nint Isa;
        internal int Flags;
        internal int Reserved;
        internal nint Invoke;
        internal nint Descriptor;
        internal nint State;
    }

    private static nint CreateString(string value) => InitString(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), value);
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint first, nint second);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint first, nint second, nint third);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)] private static partial nint InitString(nint receiver, nint selector, string value);
    [LibraryImport(SystemLibrary, EntryPoint = "_Block_copy")] private static partial nint BlockCopy(ref Block block);
    [LibraryImport(SystemLibrary, EntryPoint = "_Block_release")] private static partial void BlockRelease(nint block);
}
