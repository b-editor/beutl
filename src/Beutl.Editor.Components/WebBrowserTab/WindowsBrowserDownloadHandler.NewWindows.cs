using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;

using static Beutl.Editor.Components.WebBrowserTab.WebView2Vtable;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class WindowsBrowserDownloadHandler
{
    private readonly string _windowTarget = Guid.NewGuid().ToString("N");
    private long _windowToken, _responseToken, _contentToken, _windowMessageToken;
    private bool _observingResponses, _observingContent, _observingWindowMessages;
    private readonly List<WindowDecision> _windowDecisions = [];
    private readonly List<WindowDecision> _unmatchedWindows = [];
    private string? _scriptId;
    private Document? _pendingDocument, _committedDocument;
    internal bool HandlesNewWindows { get; private set; }

    private void AttachNewWindowHandlers(Func<Uri, Uri?, BrowserReferrerPolicy, bool> open)
    {
        // ICoreWebView2_2::add_WebResourceResponseReceived and ICoreWebView2::add_NewWindowRequested.
        _observingResponses = Subscribe<IDocumentResponseHandler>(61, new DocumentResponseHandler(this), out _responseToken) >= 0;
        // ContentLoading precedes page scripts and only fires for a new document, including on reload.
        _observingContent = Subscribe<IContentLoadingHandler>(9, new ContentLoadingHandler(this), out _contentToken) >= 0;
        _observingWindowMessages = Subscribe<IWindowMessageHandler>(34, new WindowMessageHandler(this), out _windowMessageToken) >= 0;
        HandlesNewWindows = Subscribe<INewWindowHandler>(44, new NewWindowHandler(this, open), out _windowToken) >= 0;
    }

    private unsafe void DetachNewWindowHandlers()
    {
        if (HandlesNewWindows) Unsubscribe(45, _windowToken);
        if (_observingResponses) Unsubscribe(62, _responseToken);
        if (_observingContent) Unsubscribe(10, _contentToken);
        if (_observingWindowMessages) Unsubscribe(35, _windowMessageToken);
        _windowDecisions.Clear();
        _unmatchedWindows.Clear();
        if (_scriptId != null)
        {
            fixed (char* id = _scriptId)
                _ = ((delegate* unmanaged[Stdcall]<nint, char*, int>)Method(_webView, 28))(_webView, id);
        }
    }

    internal unsafe Task InstallNewWindowScriptAsync()
    {
        var handler = new ScriptInstalled(this);
        void* callback = ComInterfaceMarshaller<IScriptInstalledHandler>.ConvertToUnmanaged(handler);
        try
        {
            fixed (char* script = WindowScript(_windowTarget))
            {
                int hr = ((delegate* unmanaged[Stdcall]<nint, char*, void*, int>)Method(_webView, 27))(_webView, script, callback);
                if (hr < 0) handler.Invoke(hr, 0);
            }
            return handler.Ready.Task;
        }
        finally { ComInterfaceMarshaller<IScriptInstalledHandler>.Free(callback); }
    }

    [GeneratedComClass]
    private sealed partial class ScriptInstalled(WindowsBrowserDownloadHandler owner) : IScriptInstalledHandler
    {
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Invoke(int hr, nint id)
        {
            if (hr >= 0) owner._scriptId = Marshal.PtrToStringUni(id);
            Ready.TrySetResult();
        }
    }

    [GeneratedComClass]
    private sealed partial class NewWindowHandler(WindowsBrowserDownloadHandler owner, Func<Uri, Uri?, BrowserReferrerPolicy, bool> open) : INewWindowHandler
    {
        public unsafe void Invoke(nint sender, nint args)
        {
            if (!Uri.TryCreate(GetString(args, 3), UriKind.Absolute, out Uri? uri)) return;
            (Uri Source, BrowserReferrerPolicy Policy)? referrer = null;
            nint named = 0;
            try
            {
                // Args3 inherits Args2::get_Name (11) and adds get_OriginalSourceFrameInfo (12).
                var iid = new Guid("842BED3C-6AD6-4DD9-B938-28C96667AD66");
                if (Marshal.QueryInterface(args, in iid, out named) >= 0)
                {
                    string? name = GetString(named, 11);
                    string? decision = name;
                    // Frames must not consume the main document's decisions, even for the same name and URL.
                    if (IsPageWindowName(name) && !owner.IsWindowTarget(name) && IsMainFrame(named))
                    {
                        // Deferring can block the message too. Use only decisions already delivered.
                        if (!TakeWindowDecision(owner._windowDecisions, name!, uri, out decision))
                            RememberWindowDecision(owner._unmatchedWindows, name!, uri, null);
                    }
                    referrer = owner.ReadWindowReferrer(named, decision);
                }
            }
            catch (Exception ex) { ReportWindowReferrerError(ex); }
            finally { if (named != 0) Marshal.Release(named); }
            if (open(uri, referrer?.Source, referrer?.Policy ?? BrowserReferrerPolicy.NoReferrer))
                _ = ((delegate* unmanaged[Stdcall]<nint, int, int>)Method(args, 6))(args, 1); // put_Handled
        }
    }

    private sealed record WindowDecision(string Name, string Url, string? Decision, long Time);

    private static bool TakeWindowDecision(List<WindowDecision> records, string name, Uri uri, out string? decision)
    {
        records.RemoveAll(record => Environment.TickCount64 - record.Time > 2000);
        int index = records.FindIndex(record => record.Name == name && record.Url == uri.AbsoluteUri);
        decision = index < 0 ? null : records[index].Decision;
        if (index < 0) return false;
        records.RemoveAt(index);
        return true;
    }

    private static void RememberWindowDecision(List<WindowDecision> records, string name, Uri uri, string? decision)
    {
        records.RemoveAll(record => Environment.TickCount64 - record.Time > 2000);
        if (records.Count >= 16) records.RemoveAt(0);
        records.Add(new WindowDecision(name, uri.AbsoluteUri, decision, Environment.TickCount64));
    }

    [GeneratedComClass]
    private sealed partial class WindowMessageHandler(WindowsBrowserDownloadHandler owner) : IWindowMessageHandler
    {
        public void Invoke(nint sender, nint args)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(GetString(args, 4) ?? "null");
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("beutlWindow", out var marker) || marker.ValueKind != JsonValueKind.String
                    || marker.GetString() != owner._windowTarget
                    || !Uri.TryCreate(GetString(args, 3), UriKind.Absolute, out Uri? source) || source != owner.GetDocument()
                    || !root.TryGetProperty("name", out var target) || target.ValueKind != JsonValueKind.String
                    || !IsPageWindowName(target.GetString())
                    || !root.TryGetProperty("url", out var address) || address.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(address.GetString(), UriKind.Absolute, out Uri? uri)
                    || !root.TryGetProperty("decision", out var value) || value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return;
                string name = target.GetString()!;
                string? decision = value.GetString();
                if (decision != null && !owner.IsWindowTarget(decision)) return;
                // A late message belongs to the window already handled without a referrer, not the next one.
                if (!TakeWindowDecision(owner._unmatchedWindows, name, uri, out _))
                {
                    // Frame navigations never take their decisions, so a newer one for the same name and URL replaces them.
                    owner._windowDecisions.RemoveAll(record => record.Name == name && record.Url == uri.AbsoluteUri);
                    RememberWindowDecision(owner._windowDecisions, name, uri, decision);
                }
            }
            catch (Exception ex) { ReportWindowReferrerError(ex); }
        }
    }

    private static void ReportWindowReferrerError(Exception error) =>
        Trace.TraceError("WebView2 could not read a new window's referrer: {0}", error.Message);

    private static bool IsPageWindowName(string? name) => !string.IsNullOrEmpty(name) && !name.StartsWith('_');

    private bool IsWindowTarget(string? name) =>
        name == _windowTarget || name == _windowTarget + "-document" || name == _windowTarget + "-same-origin";

    private (Uri Source, BrowserReferrerPolicy Policy)? ReadWindowReferrer(nint named, string? decision)
    {
        if (!IsWindowTarget(decision)) return null;
        bool inherit = decision == _windowTarget + "-document";
        bool sameOrigin = decision == _windowTarget + "-same-origin";
        nint frame = GetObject(named, 12);
        if (frame == 0) return null;
        try
        {
            if (!Uri.TryCreate(GetString(frame, 4), UriKind.Absolute, out Uri? source)) return null;
            if (!inherit) return (source, sameOrigin ? BrowserReferrerPolicy.SameOrigin : BrowserReferrerPolicy.Origin);
            // History changes the displayed URL, but the committed document keeps its HTTP response policy.
            if (source != GetDocument() || DocumentReferrerPolicy is not { } policy) return null;
            return (source, policy);
        }
        finally { Marshal.Release(frame); }
    }

    private static bool IsMainFrame(nint named)
    {
        nint frame = 0, info = 0;
        try
        {
            frame = GetObject(named, 12);
            if (frame == 0) return false;
            var iid = new Guid("56F85CFA-72C4-11EE-B962-0242AC120002"); // ICoreWebView2FrameInfo2
            if (Marshal.QueryInterface(frame, in iid, out info) < 0) return false;
            return GetValue<int>(info, 7) == 1; // COREWEBVIEW2_FRAME_KIND_MAIN_FRAME
        }
        finally
        {
            if (info != 0) Marshal.Release(info);
            if (frame != 0) Marshal.Release(frame);
        }
    }

    // The response may arrive before or after ContentLoading. Both callbacks update the same document.
    private sealed class Document(Uri address)
    {
        internal Uri Address { get; } = address;
        internal BrowserReferrerPolicy? Policy { get; set; }
    }

    [GeneratedComClass]
    private sealed partial class ContentLoadingHandler(WindowsBrowserDownloadHandler owner) : IContentLoadingHandler
    {
        public void Invoke(nint sender, nint args)
        {
            owner._windowDecisions.Clear();
            owner._unmatchedWindows.Clear();
            owner._committedDocument = owner._pendingDocument is { } pending && pending.Address == owner.GetDocument() ? pending : null;
        }
    }

    [GeneratedComClass]
    private sealed partial class DocumentResponseHandler(WindowsBrowserDownloadHandler owner) : IDocumentResponseHandler
    {
        public unsafe void Invoke(nint sender, nint args)
        {
            owner.RecordDownloadResponse(args);
            nint request = 0, requestHeaders = 0, response = 0, responseHeaders = 0;
            try
            {
                if (owner._pendingDocument is not { Policy: null } pending) return;
                request = GetObject(args, 3);
                if (!Uri.TryCreate(GetString(request, 3), UriKind.Absolute, out Uri? page) || page != pending.Address) return;
                // Subresources and inline frames can have different policies, even at the same URL.
                requestHeaders = GetObject(request, 9);
                if (GetHeader(requestHeaders, "Sec-Fetch-Dest") != "document") return;
                response = GetObject(args, 4);
                responseHeaders = GetObject(response, 3);
                pending.Policy = ReadDocumentPolicy(responseHeaders);
            }
            catch (Exception ex)
            {
                owner.BeginDocument(null);
                Trace.TraceError("WebView2 could not read the document's referrer policy: {0}", ex.Message);
            }
            finally
            {
                if (responseHeaders != 0) Marshal.Release(responseHeaders);
                if (response != 0) Marshal.Release(response);
                if (requestHeaders != 0) Marshal.Release(requestHeaders);
                if (request != 0) Marshal.Release(request);
            }
        }

        private static unsafe BrowserReferrerPolicy ReadDocumentPolicy(nint headers)
        {
            // GetHeader returns only the first value. Chromium applies the last recognized token across all lines.
            nint iterator = 0;
            fixed (char* key = "Referrer-Policy")
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Method(headers, 6))(headers, key, &iterator));
            try
            {
                BrowserReferrerPolicy policy = BrowserReferrerPolicy.Origin;
                int more;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(iterator, 4))(iterator, &more));
                while (more != 0)
                {
                    nint name = 0, value = 0;
                    try
                    {
                        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, nint*, int>)Method(iterator, 3))(iterator, &name, &value));
                        policy = BrowserMediaDownload.ParseReferrerPolicyHeader(Marshal.PtrToStringUni(value), policy) ?? policy;
                    }
                    finally { Marshal.FreeCoTaskMem(name); Marshal.FreeCoTaskMem(value); }
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(iterator, 5))(iterator, &more));
                }
                return policy;
            }
            finally { Marshal.Release(iterator); }
        }
    }

    // WebView2 omits Referer from NewWindowRequested. Carry the decision in the native request's
    // target name when safe, or post it before a name that might belong to an existing frame is used.
    private static string WindowScript(string target) => $$"""
        (() => {
            if (!['https:', 'http:'].includes(location.protocol) || self.origin === 'null') return;
            const allowed = '{{target}}';
            const policyValue = document.createElement('a');
            let documentPolicy = '';
            const observedMetas = new WeakSet();
            function* metasIn(nodes) {
                for (const node of nodes) {
                    if (node instanceof HTMLMetaElement) yield node;
                    if (node.firstElementChild) {
                        for (const meta of node.getElementsByTagName('meta')) {
                            if (meta instanceof HTMLMetaElement) yield meta;
                        }
                    }
                }
            }
            const legacy = { never: 'no-referrer', always: 'unsafe-url', default: 'strict-origin-when-cross-origin',
                'origin-when-crossorigin': 'origin-when-cross-origin' };

            // A removed or invalid meta leaves the last applied policy in effect. Several attributes can change
            // in the same script, so replay the values at each mutation, including before a synchronous popup.
            function applyChanges(records) {
                if (!records.length) return;
                const states = new Map();
                const updates = new Map();
                const renamedReferrers = new Set();
                for (let i = records.length - 1; i >= 0; i--) {
                    const record = records[i], meta = record.target;
                    if (record.type !== 'attributes' || !(meta instanceof HTMLMetaElement)) continue;
                    if (record.attributeName === 'name' && record.oldValue?.toLowerCase() === 'referrer') renamedReferrers.add(meta);
                    const state = states.get(meta) ?? { name: meta.name, content: meta.content };
                    updates.set(record, state);
                    states.set(meta, { ...state, [record.attributeName]: record.oldValue ?? '' });
                }
                function applyMeta(meta) {
                    const state = states.get(meta) ?? meta;
                    if (state.name.toLowerCase() !== 'referrer') return;
                    const value = state.content.replace(/^[\t\n\f\r ]+|[\t\n\f\r ]+$/g, '').toLowerCase();
                    policyValue.referrerPolicy = Object.hasOwn(legacy, value) ? legacy[value] : value;
                    if (policyValue.referrerPolicy) documentPolicy = document.contains(meta) ? policyValue.referrerPolicy : 'no-referrer';
                }
                for (const record of records) {
                    if (updates.has(record)) {
                        states.set(record.target, updates.get(record));
                        applyMeta(record.target);
                    } else {
                        for (const meta of metasIn(record.removedNodes)) {
                            // An inserted wrapper may already have lost this meta when its addition is replayed.
                            if (!observedMetas.delete(meta) && (meta.name.toLowerCase() === 'referrer' || renamedReferrers.has(meta)))
                                documentPolicy = 'no-referrer';
                        }
                        for (const meta of metasIn(record.addedNodes)) {
                            // The observer can still report additions inside a removed subtree until delivery.
                            if (document.contains(meta)) observedMetas.add(meta);
                            else observedMetas.delete(meta);
                            applyMeta(meta);
                        }
                    }
                }
            }
            const observer = new MutationObserver(applyChanges);
            observer.observe(document, {subtree:true, childList:true, attributes:true, attributeOldValue:true, attributeFilter:['name','content']});
            // Download links need the applied meta policy, even after its element is removed.
            Object.defineProperty(window, '__beutlReferrerPolicy', {
                value: () => {
                    applyChanges(observer.takeRecords());
                    return documentPolicy;
                }
            });

            function windowTarget(url, link) {
                if (!['https:', 'http:'].includes(url.protocol) || link?.rel.toLowerCase().split(/\s+/).includes('noreferrer')) return null;
                applyChanges(observer.takeRecords());
                const policy = link?.referrerPolicy || documentPolicy;
                if (!policy) return self === top ? allowed + '-document' : null;
                if (policy === 'same-origin') return url.origin === self.origin ? allowed + '-same-origin' : null;
                return policy === 'no-referrer' ? null : allowed;
            }
            function opensNewContext(target, noopener) {
                if (!target) return false;
                if (target.toLowerCase() === '_blank') return true;
                if (target.startsWith('_')) return false;
                if (noopener) return true;
                // Beutl opens popups in independent WebViews. Keep the native name if a frame might already own it.
                try {
                    if (top.opener) return null;
                    const frames = [top];
                    while (frames.length) {
                        const frame = frames.pop();
                        if (frame.name === target) return false;
                        for (let i = 0; i < frame.length; i++) frames.push(frame[i]);
                    }
                    return true;
                } catch { return null; } // A cross-origin frame can hide a named target.
            }
            function postWindow(name, url, decision) {
                // Frames without the host bridge keep the native name and the host's no-referrer fallback.
                if (name && !name.startsWith('_')) window.chrome?.webview?.postMessage({beutlWindow: allowed, name, url, decision});
            }
            const open = window.open;
            window.open = function(url, target, features) {
                if (this && this !== window || !url) return Reflect.apply(open, this, arguments);
                let destination;
                try { destination = new URL(url, document.baseURI); } catch { return Reflect.apply(open, this, arguments); }
                const flags = new Map();
                const tokens = /([^\t\n\f\r ,=]+)(?:[\t\n\f\r ]*=[\t\n\f\r =]*([^\t\n\f\r ,=]*))?/g;
                for (const [, key, value] of String(features ?? '').toLowerCase().matchAll(tokens)) {
                    if (key === 'noopener' || key === 'noreferrer')
                        flags.set(key, !value || value === 'yes' || value === 'true' || !!parseInt(value, 10));
                }
                const destinationTarget = target === undefined || target === '' ? '_blank' : String(target);
                const context = opensNewContext(destinationTarget, flags.get('noopener'));
                const decision = !flags.get('noreferrer') ? windowTarget(destination) : null;
                const name = context === true && decision;
                if (!name && context !== false) postWindow(destinationTarget, destination.href, decision);
                return name ? open.call(window, destination.href, name, features) : Reflect.apply(open, this, arguments);
            };
            const pendingTargets = new WeakMap();
            function markLink(link) {
                const pending = pendingTargets.get(link);
                const target = link.getAttribute('target');
                const original = pending?.assigned === target ? pending.original : target;
                const effectiveTarget = original ?? document.querySelector('base[target]')?.target ?? '';
                const noopener = link.rel.toLowerCase().split(/[\t\n\f\r ]+/).includes('noopener');
                const context = opensNewContext(effectiveTarget, noopener);
                const decision = windowTarget(new URL(link.href), link);
                const name = context === true && decision;
                if (!name && context !== false) postWindow(effectiveTarget, link.href, decision);
                if (!name && !pending) return;
                const mark = {original, assigned: name || effectiveTarget};
                pendingTargets.set(link, mark);
                link.target = mark.assigned;
                setTimeout(() => {
                    if (pendingTargets.get(link) !== mark) return;
                    pendingTargets.delete(link);
                    if (link.target !== mark.assigned) return;
                    if (original === null) link.removeAttribute('target');
                    else link.target = original;
                }, 0);
            }
            window.addEventListener('click', event => {
                if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                const path = event.composedPath();
                const link = path.find(node => node instanceof HTMLAnchorElement || node instanceof HTMLAreaElement);
                if (!link) return;
                // stopPropagation still activates the link. Read after the page's handlers on the node where dispatch ends.
                const listeners = new AbortController();
                const afterPage = current => {
                    if (current !== event) return;
                    if (!event.cancelBubble && (event.currentTarget !== window || event.eventPhase !== Event.BUBBLING_PHASE)) return;
                    listeners.abort();
                    if (!event.defaultPrevented && link.hasAttribute('href') && !link.hasAttribute('download'))
                        markLink(link);
                };
                for (const node of path) {
                    node.addEventListener('click', afterPage, {capture:true, signal:listeners.signal});
                    node.addEventListener('click', afterPage, {signal:listeners.signal});
                }
                setTimeout(() => listeners.abort(), 0);
            }, true);
        })()
        """;
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("D4C185FE-C81C-4989-97AF-2D3FA7AB5651")]
internal partial interface INewWindowHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("364471E7-F2BE-4910-BDBA-D72077D51C4B")]
internal partial interface IContentLoadingHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("7DE9898A-24F5-40C3-A2DE-D4F458E69828")]
internal partial interface IDocumentResponseHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("B99369F3-9B11-47B5-BC6F-8E7895FCEA17")]
internal partial interface IScriptInstalledHandler
{
    void Invoke(int hr, nint id);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("57213F19-00E6-49FA-8E07-898EA01ECBD2")]
internal partial interface IWindowMessageHandler
{
    void Invoke(nint sender, nint args);
}
