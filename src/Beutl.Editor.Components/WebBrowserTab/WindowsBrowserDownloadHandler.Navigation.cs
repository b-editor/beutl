using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using static Beutl.Editor.Components.WebBrowserTab.WebView2Vtable;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class WindowsBrowserDownloadHandler
{
    private long _navigationStartingToken, _navigationCompletedToken, _historyChangedToken;
    private bool _observingNavigationStarting, _observingNavigationCompleted, _observingHistory;
    private readonly HashSet<ulong> _canceledNavigationIds = [];
    private (ulong Id, Uri? Uri)? _activeNavigation;
    private bool _awaitingHistoryLoad;
    internal bool HandlesNavigation { get; private set; }

    private void AttachNavigationHandlers(Action<Uri, bool> onNavigationCompleted, Action<Uri?> onHistoryChanged)
    {
        try
        {
            _observingNavigationStarting = Subscribe<INavigationStartingHandler>(7, new NavigationStartingHandler(this), out _navigationStartingToken) >= 0;
            _observingNavigationCompleted = Subscribe<INavigationCompletedHandler>(15, new NavigationCompletedHandler(this, onNavigationCompleted), out _navigationCompletedToken) >= 0;
            _observingHistory = Subscribe<IHistoryChangedHandler>(13, new HistoryChangedHandler(this, onHistoryChanged), out _historyChangedToken) >= 0;
            HandlesNavigation = _observingNavigationStarting && _observingNavigationCompleted && _observingHistory;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WebView2 navigation handlers could not be installed: {0}", ex);
        }
        // Without all three events, keep Avalonia's completion and history behavior.
        if (!HandlesNavigation) DetachNavigationHandlers();
    }

    private void DetachNavigationHandlers()
    {
        if (_observingNavigationStarting) Unsubscribe(8, _navigationStartingToken);
        if (_observingNavigationCompleted) Unsubscribe(16, _navigationCompletedToken);
        if (_observingHistory) Unsubscribe(14, _historyChangedToken);
        _observingNavigationStarting = _observingNavigationCompleted = _observingHistory = HandlesNavigation = false;
        _activeNavigation = null;
        _canceledNavigationIds.Clear();
        _awaitingHistoryLoad = false;
    }

    private unsafe void OnNavigationStarting(nint args)
    {
        _awaitingHistoryLoad = false;
        ulong id;
        int canceled;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, ulong*, int>)Method(args, 9))(args, &id));
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(args, 7))(args, &canceled));
        // Avalonia runs first, so this includes cancellation by the tab's NavigationStarted handler.
        if (canceled != 0)
        {
            _canceledNavigationIds.Add(id);
            return;
        }
        Uri.TryCreate(GetString(args, 3), UriKind.Absolute, out Uri? uri);
        // Redirects repeat the ID; a new ID supersedes the previous main-frame load.
        _activeNavigation = (id, uri);
    }

    private unsafe void OnNavigationCompleted(nint args, Action<Uri, bool> completed)
    {
        ulong id;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, ulong*, int>)Method(args, 5))(args, &id));
        var active = _activeNavigation;
        if (active?.Id == id) _activeNavigation = null;
        // A committed document can complete successfully after the next navigation has started.
        if (_canceledNavigationIds.Remove(id) || active is { } navigation && navigation.Id != id) return;

        int success;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(args, 3))(args, &success));
        // Avalonia also reports Source, including on failures and downloads. A start may predate attachment.
        if ((GetDocument() ?? active?.Uri) is { } request) completed(request, success != 0);
    }

    internal bool NavigateHistory(Func<bool> navigate)
    {
        _awaitingHistoryLoad = true;
        if (navigate()) return true;
        _awaitingHistoryLoad = false;
        return false;
    }

    private void OnHistoryChanged(Action<Uri?> changed)
    {
        Uri? restored = null;
        // Same-document Back/Forward raises only SourceChanged followed by HistoryChanged, with no load events.
        if (_awaitingHistoryLoad)
        {
            _awaitingHistoryLoad = false;
            restored = GetDocument();
        }
        changed(restored);
    }

    [GeneratedComClass]
    private sealed partial class NavigationStartingHandler(WindowsBrowserDownloadHandler owner) : INavigationStartingHandler
    {
        public void Invoke(nint sender, nint args) => owner.OnNavigationStarting(args);
    }

    [GeneratedComClass]
    private sealed partial class NavigationCompletedHandler(WindowsBrowserDownloadHandler owner, Action<Uri, bool> completed) : INavigationCompletedHandler
    {
        public void Invoke(nint sender, nint args) => owner.OnNavigationCompleted(args, completed);
    }

    [GeneratedComClass]
    private sealed partial class HistoryChangedHandler(WindowsBrowserDownloadHandler owner, Action<Uri?> changed) : IHistoryChangedHandler
    {
        public void Invoke(nint sender, nint args) => owner.OnHistoryChanged(changed);
    }
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("9ADBE429-F36D-432B-9DDC-F8881FBD76E3")]
internal partial interface INavigationStartingHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("D33A35BF-1C49-4F98-93AB-006E0533FE1C")]
internal partial interface INavigationCompletedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("C79A420C-EFD9-4058-9295-3E8B4BCAB645")]
internal partial interface IHistoryChangedHandler
{
    void Invoke(nint sender, nint args);
}
