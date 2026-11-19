using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using static Beutl.Editor.Components.WebBrowserTab.WebView2Vtable;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class WindowsBrowserDownloadHandler
{
    private readonly string _windowTarget = Guid.NewGuid().ToString("N");
    private long _windowToken, _responseToken, _contentToken;
    private bool _observingResponses, _observingContent;
    private string? _scriptId;
    private Document? _pendingDocument, _committedDocument;
    internal bool HandlesNewWindows { get; private set; }

    private void AttachNewWindowHandlers(Func<Uri, Uri?, BrowserReferrerPolicy, bool> open)
    {
        // ICoreWebView2_2::add_WebResourceResponseReceived and ICoreWebView2::add_NewWindowRequested.
        _observingResponses = Subscribe<IDocumentResponseHandler>(61, new DocumentResponseHandler(this), out _responseToken) >= 0;
        // ContentLoading precedes page scripts and only fires for a new document, including on reload.
        _observingContent = Subscribe<IContentLoadingHandler>(9, new ContentLoadingHandler(this), out _contentToken) >= 0;
        HandlesNewWindows = Subscribe<INewWindowHandler>(44, new NewWindowHandler(this, open), out _windowToken) >= 0;
    }

    private unsafe void DetachNewWindowHandlers()
    {
        if (HandlesNewWindows) Unsubscribe(45, _windowToken);
        if (_observingResponses) Unsubscribe(62, _responseToken);
        if (_observingContent) Unsubscribe(10, _contentToken);
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
            try { referrer = owner.ReadWindowReferrer(args); }
            catch (Exception ex) { Trace.TraceError("WebView2 could not read a new window's referrer: {0}", ex.Message); }
            if (open(uri, referrer?.Source, referrer?.Policy ?? BrowserReferrerPolicy.NoReferrer))
                _ = ((delegate* unmanaged[Stdcall]<nint, int, int>)Method(args, 6))(args, 1); // put_Handled
        }
    }

    private (Uri Source, BrowserReferrerPolicy Policy)? ReadWindowReferrer(nint args)
    {
        // Args3 inherits Args2::get_Name (11) and adds get_OriginalSourceFrameInfo (12).
        var iid = new Guid("842BED3C-6AD6-4DD9-B938-28C96667AD66");
        if (Marshal.QueryInterface(args, in iid, out nint named) < 0) return null;
        try
        {
            string? name = GetString(named, 11);
            bool inherit = name == _windowTarget + "-document";
            bool sameOrigin = name == _windowTarget + "-same-origin";
            if (!inherit && !sameOrigin && name != _windowTarget) return null;
            nint frame = GetObject(named, 12);
            if (frame == 0) return null;
            try
            {
                if (!Uri.TryCreate(GetString(frame, 4), UriKind.Absolute, out Uri? source)) return null;
                if (!inherit) return (source, sameOrigin ? BrowserReferrerPolicy.SameOrigin : BrowserReferrerPolicy.Origin);
                // History changes the displayed URL, but the committed document keeps its HTTP response policy.
                if (_committedDocument is not { Policy: { } policy } document || source != GetDocument()
                    || !BrowserMediaDownload.IsSameOrigin(document.Address, source)) return null;
                return (source, policy);
            }
            finally { Marshal.Release(frame); }
        }
        finally { Marshal.Release(named); }
    }

    // An attachment can finish a navigation without replacing the displayed document.
    internal void BeginDocument(Uri? request) => _pendingDocument = request == null ? null : new Document(request);

    // The response may arrive before or after ContentLoading. Both callbacks update the same document.
    private sealed class Document(Uri address)
    {
        internal Uri Address { get; } = address;
        internal BrowserReferrerPolicy? Policy { get; set; }
    }

    [GeneratedComClass]
    private sealed partial class ContentLoadingHandler(WindowsBrowserDownloadHandler owner) : IContentLoadingHandler
    {
        public void Invoke(nint sender, nint args) =>
            owner._committedDocument = owner._pendingDocument is { } pending && pending.Address == owner.GetDocument() ? pending : null;
    }

    [GeneratedComClass]
    private sealed partial class DocumentResponseHandler(WindowsBrowserDownloadHandler owner) : IDocumentResponseHandler
    {
        public unsafe void Invoke(nint sender, nint args)
        {
            nint request = 0, requestHeaders = 0, response = 0, responseHeaders = 0;
            try
            {
                request = GetObject(args, 3);
                if (!Uri.TryCreate(GetString(request, 3), UriKind.Absolute, out Uri? page)) return;
                // Subresources and inline frames can have different policies, even at the same URL.
                requestHeaders = GetObject(request, 9);
                if (GetHeader(requestHeaders, "Sec-Fetch-Dest") != "document") return;
                response = GetObject(args, 4);
                if (BrowserMediaDownload.IsHttpUri(page))
                {
                    try
                    {
                        int status;
                        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(response, 4))(response, &status));
                        owner.RecordDownloadResponse(page, status == 200 ? GetString(request, 5) : null);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("WebView2 could not read a download response's method: {0}", ex.Message);
                    }
                }
                if (owner._pendingDocument is not { Policy: null } pending || page != pending.Address) return;
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
                        foreach (string token in (Marshal.PtrToStringUni(value) ?? "").Split(','))
                            policy = token.Trim().ToLowerInvariant() switch
                            {
                                "no-referrer" => BrowserReferrerPolicy.NoReferrer,
                                "same-origin" => BrowserReferrerPolicy.SameOrigin,
                                "origin" or "strict-origin" or "origin-when-cross-origin" or "strict-origin-when-cross-origin"
                                    or "no-referrer-when-downgrade" or "unsafe-url" => BrowserReferrerPolicy.Origin,
                                _ => policy
                            };
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
    // target name, so two windows to the same URL cannot consume one another's policy.
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
                    if (top.opener) return false;
                    const frames = [top];
                    while (frames.length) {
                        const frame = frames.pop();
                        if (frame.name === target) return false;
                        for (let i = 0; i < frame.length; i++) frames.push(frame[i]);
                    }
                    return true;
                } catch { return false; } // A cross-origin frame can hide a named target.
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
                const name = !flags.get('noreferrer') && opensNewContext(destinationTarget, flags.get('noopener')) && windowTarget(destination);
                return name ? open.call(window, destination.href, name, features) : Reflect.apply(open, this, arguments);
            };
            const pendingTargets = new WeakMap();
            function markLink(link) {
                const pending = pendingTargets.get(link);
                const target = link.getAttribute('target');
                const original = pending?.assigned === target ? pending.original : target;
                const effectiveTarget = original ?? document.querySelector('base[target]')?.target ?? '';
                const noopener = link.rel.toLowerCase().split(/[\t\n\f\r ]+/).includes('noopener');
                const name = opensNewContext(effectiveTarget, noopener) && windowTarget(new URL(link.href), link);
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
