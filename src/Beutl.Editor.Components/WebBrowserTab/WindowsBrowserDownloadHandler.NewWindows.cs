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
    private Uri? _expectedDocument, _committedDocument;
    private (Uri Page, BrowserReferrerPolicy Policy)? _documentPolicy;
    internal bool HandlesNewWindows { get; private set; }

    private void AttachNewWindowHandlers(Func<Uri, Uri?, bool> open)
    {
        // ICoreWebView2_2::add_WebResourceResponseReceived and ICoreWebView2::add_NewWindowRequested.
        _observingResponses = Subscribe<IDocumentResponseHandler>(61, new DocumentResponseHandler(this), out _responseToken) >= 0;
        // ContentLoading precedes page scripts and only fires for a new document, including on reload.
        _observingContent = Subscribe<IContentLoadingHandler>(9, new ContentLoadingHandler(this), out _contentToken) >= 0;
        HandlesNewWindows = Subscribe<INewWindowHandler>(44, new NewWindowHandler(this, open), out _windowToken) >= 0;
    }

    private unsafe void DetachNewWindowHandlers()
    {
        if (HandlesNewWindows)
            _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Method(_webView, 45))(_webView, _windowToken);
        if (_observingResponses)
            _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Method(_webView, 62))(_webView, _responseToken);
        if (_observingContent)
            _ = ((delegate* unmanaged[Stdcall]<nint, long, int>)Method(_webView, 10))(_webView, _contentToken);
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
    private sealed partial class NewWindowHandler(WindowsBrowserDownloadHandler owner, Func<Uri, Uri?, bool> open) : INewWindowHandler
    {
        public unsafe void Invoke(nint sender, nint args)
        {
            if (!Uri.TryCreate(GetString(args, 3), UriKind.Absolute, out Uri? uri)) return;
            Uri? referrer = null;
            try { referrer = owner.ReadWindowReferrer(args, uri); }
            catch (Exception ex) { Trace.TraceError("WebView2 could not read a new window's referrer: {0}", ex.Message); }
            if (open(uri, referrer))
                _ = ((delegate* unmanaged[Stdcall]<nint, int, int>)Method(args, 6))(args, 1); // put_Handled
        }
    }

    private Uri? ReadWindowReferrer(nint args, Uri uri)
    {
        // Args3 inherits Args2::get_Name (11) and adds get_OriginalSourceFrameInfo (12).
        var iid = new Guid("842BED3C-6AD6-4DD9-B938-28C96667AD66");
        if (Marshal.QueryInterface(args, in iid, out nint named) < 0) return null;
        try
        {
            string? name = GetString(named, 11);
            bool inherit = name == _windowTarget + "-document";
            if (!inherit && name != _windowTarget) return null;
            nint frame = GetObject(named, 12);
            if (frame == 0) return null;
            try
            {
                if (!Uri.TryCreate(GetString(frame, 4), UriKind.Absolute, out Uri? source)) return null;
                if (!inherit) return source;
                if (_documentPolicy is not { } document) return null;
                // A same-document history change updates the frame URL while its response policy still applies.
                if (document.Page != source && (document.Page != _committedDocument
                    || !BrowserMediaDownload.IsSameOrigin(document.Page, source) || source != GetDocument())) return null;
                return BrowserMediaDownload.NormalizeReferrer(source, uri, document.Policy);
            }
            finally { Marshal.Release(frame); }
        }
        finally { Marshal.Release(named); }
    }

    internal void BeginDocument(Uri? request)
    {
        _expectedDocument = request;
        _committedDocument = null;
        _documentPolicy = null;
    }

    [GeneratedComClass]
    private sealed partial class ContentLoadingHandler(WindowsBrowserDownloadHandler owner) : IContentLoadingHandler
    {
        public void Invoke(nint sender, nint args) => owner._committedDocument = owner.GetDocument();
    }

    [GeneratedComClass]
    private sealed partial class DocumentResponseHandler(WindowsBrowserDownloadHandler owner) : IDocumentResponseHandler
    {
        public unsafe void Invoke(nint sender, nint args)
        {
            nint request = 0, requestHeaders = 0, response = 0, responseHeaders = 0;
            try
            {
                if (owner._expectedDocument == null) return;
                request = GetObject(args, 3);
                if (!Uri.TryCreate(GetString(request, 3), UriKind.Absolute, out Uri? page) || page != owner._expectedDocument) return;
                // Subresources and inline frames can have different policies, even at the same URL.
                requestHeaders = GetObject(request, 9);
                if (!IsDocumentRequest(requestHeaders)) return;
                response = GetObject(args, 4);
                responseHeaders = GetObject(response, 3);
                owner._documentPolicy = (page, ReadDocumentPolicy(responseHeaders));
                owner._expectedDocument = null;
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

        private static unsafe bool IsDocumentRequest(nint headers)
        {
            nint value = 0;
            fixed (char* name = "Sec-Fetch-Dest")
            {
                int hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Method(headers, 3))(headers, name, &value);
                try { return hr >= 0 && Marshal.PtrToStringUni(value) == "document"; }
                finally { Marshal.FreeCoTaskMem(value); }
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
            const legacy = { never: 'no-referrer', always: 'unsafe-url', default: 'strict-origin-when-cross-origin',
                'origin-when-crossorigin': 'origin-when-cross-origin' };

            // A removed or invalid meta leaves the last applied policy in effect. Several attributes can change
            // in the same script, so replay the values at each mutation, including before a synchronous popup.
            function applyChanges(records) {
                if (!records.length) return;
                const states = new Map();
                const updates = new Map();
                for (let i = records.length - 1; i >= 0; i--) {
                    const record = records[i], meta = record.target;
                    if (record.type !== 'attributes' || !(meta instanceof HTMLMetaElement)) continue;
                    const state = states.get(meta) ?? { name: meta.name, content: meta.content };
                    updates.set(record, state);
                    states.set(meta, { ...state, [record.attributeName]: record.oldValue ?? '' });
                }
                function applyMeta(meta) {
                    if (!(meta instanceof HTMLMetaElement)) return;
                    const state = states.get(meta) ?? meta;
                    if (state.name.toLowerCase() !== 'referrer') return;
                    const value = state.content.replace(/^[\t\n\f\r ]+|[\t\n\f\r ]+$/g, '').toLowerCase();
                    policyValue.referrerPolicy = Object.hasOwn(legacy, value) ? legacy[value] : value;
                    if (policyValue.referrerPolicy) documentPolicy = meta.isConnected ? policyValue.referrerPolicy : 'no-referrer';
                }
                for (const record of records) {
                    if (updates.has(record)) {
                        states.set(record.target, updates.get(record));
                        applyMeta(record.target);
                    } else {
                        for (const node of record.addedNodes) {
                            applyMeta(node);
                            if (node.firstElementChild) {
                                for (const meta of node.getElementsByTagName('meta')) applyMeta(meta);
                            }
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
                return policy === 'no-referrer' || policy === 'same-origin' && url.origin !== self.origin ? null : allowed;
            }
            const open = window.open;
            window.open = function(url, target, features) {
                if (this && this !== window || !url || target && String(target).toLowerCase() !== '_blank')
                    return Reflect.apply(open, this, arguments);
                let destination;
                try { destination = new URL(url, document.baseURI); } catch { return Reflect.apply(open, this, arguments); }
                const flags = String(features ?? '').matchAll(/(?:^|[\s,=])noreferrer(?=[\s,=]|$)(?:\s*=\s*([^\s,=]+))?/gi);
                const suppress = Array.from(flags).some(([, value]) => !['0', 'no', 'false'].includes(value?.toLowerCase()));
                const name = !suppress && windowTarget(destination);
                return name ? open.call(window, destination.href, name, features) : Reflect.apply(open, this, arguments);
            };
            const pendingTargets = new WeakMap();
            function markLink(link) {
                const pending = pendingTargets.get(link);
                const target = link.getAttribute('target');
                const original = pending?.assigned === target ? pending.original : target;
                if ((original ?? document.querySelector('base[target]')?.target)?.toLowerCase() !== '_blank') return;
                const name = windowTarget(new URL(link.href), link);
                if (!name && !pending) return;
                const mark = {original, assigned: name || '_blank'};
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
                const link = event.composedPath().find(node => node instanceof HTMLAnchorElement || node instanceof HTMLAreaElement);
                if (!link) return;
                // Read after page click handlers, including handlers that cancel the click or change its policy.
                const afterPage = current => {
                    if (current === event && !event.defaultPrevented && link.hasAttribute('href') && !link.hasAttribute('download'))
                        markLink(link);
                };
                window.addEventListener('click', afterPage);
                setTimeout(() => window.removeEventListener('click', afterPage), 0);
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
