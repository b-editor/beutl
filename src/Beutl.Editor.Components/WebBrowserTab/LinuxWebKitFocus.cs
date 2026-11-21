using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;

using static Beutl.Editor.Components.WebBrowserTab.LinuxWebKit;

namespace Beutl.Editor.Components.WebBrowserTab;

// Avalonia's GTK X11 WebView embeds WebKitGTK's GtkWindow inside an X11 child window while leaving
// X11 input focus on Avalonia's top-level window. Because GTK3 consumes XInput2 XI_KeyPress instead
// of core X11 KeyPressMask and leaves do_not_propagate_mask unset, every key typed in the browser
// also propagates as a core X11 KeyPress to Avalonia's top-level window and triggers Beutl shortcuts.
// Blocking core key propagation on the WebKitGTK X11 window stops that dual delivery, and syncing
// X11 input focus on EWMH window managers keeps keyboard and IME input in WebKitGTK even when the
// pointer moves outside the browser surface.
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxWebKitFocus : IDisposable
{
    private const nint KeyPressMask = 1 << 0;
    private const nint KeyReleaseMask = 1 << 1;
    private const nint KeyPropagationMask = KeyPressMask | KeyReleaseMask;
    private const nuint CWDontPropagate = 1u << 12;
    private const int IsViewable = 2;
    private const int RevertToParent = 2;
    private const nuint CurrentTime = 0;
    private const uint GdkShiftMask = 1u << 0;
    private const uint GdkControlMask = 1u << 2;
    private const uint GdkMod1Mask = 1u << 3;
    private const uint GdkSuperMask = 1u << 26;

    private readonly NativeWebView _webView;
    private readonly nint _gtkWebView;
    private readonly nint _x11Window;
    private readonly Action<Key, KeyModifiers>? _onBrowserShortcut;
    private readonly GCHandle _selfHandle;
    private Window? _hostWindow;
    private nint _hostXid;
    private nuint _buttonPressSignal;
    private nuint _keyPressSignal;
    private bool _ewmhFocusTracking;
    private volatile bool _disposed;

    private LinuxWebKitFocus(NativeWebView webView, nint gtkWebView, nint x11Window, Action<Key, KeyModifiers>? onBrowserShortcut)
    {
        _webView = webView;
        _gtkWebView = gtkWebView;
        _x11Window = x11Window;
        _onBrowserShortcut = onBrowserShortcut;
        _selfHandle = GCHandle.Alloc(this);

        _webView.GotFocus += OnWebViewGotFocus;
        _webView.LostFocus += OnWebViewLostFocus;
        _webView.AttachedToVisualTree += OnWebViewAttachedToVisualTree;
        _webView.DetachedFromVisualTree += OnWebViewDetachedFromVisualTree;
        AttachHostWindow();

        bool initiallyFocused = _webView.IsFocused;
        _ = OnGlibAsync(() => InitializeOnGlib(initiallyFocused));
    }

    internal static LinuxWebKitFocus? TryAttach(
        NativeWebView webView,
        IPlatformHandle? handle,
        Action<Key, KeyModifiers>? onBrowserShortcut = null)
    {
        if (handle is not IGtkWebViewPlatformHandle gtk
            || handle.HandleDescriptor != "XID"
            || handle.Handle == 0
            || gtk.WebKitWebView == 0)
        {
            return null;
        }

        try
        {
            if (!NativeLibrary.TryLoad("libgdk-3.so.0", out _) || !NativeLibrary.TryLoad("libX11.so.6", out _))
            {
                return null;
            }

            // Retain before dispatch; release on GLib too because the last unref can destroy a widget.
            nint retained = ObjectRef(gtk.WebKitWebView);
            return new LinuxWebKitFocus(webView, retained, handle.Handle, onBrowserShortcut);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal static Task<nint?> ReadDoNotPropagateMaskAsync(nint x11Window)
    {
        if (x11Window == 0) return Task.FromResult<nint?>(null);

        var completion = new TaskCompletionSource<nint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = OnGlibAsync(() =>
        {
            try
            {
                if (!TryGetXDisplay(out nint gdkDisplay, out nint xDisplay))
                {
                    completion.TrySetResult(null);
                    return;
                }

                nint? mask = null;
                WithErrorTrap(gdkDisplay, xDisplay, () =>
                {
                    if (XGetWindowAttributes(xDisplay, x11Window, out XWindowAttributes attrs) != 0)
                    {
                        mask = attrs.DoNotPropagateMask;
                    }
                });
                completion.TrySetResult(mask);
            }
            catch
            {
                completion.TrySetResult(null);
            }
        });
        return completion.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _webView.GotFocus -= OnWebViewGotFocus;
        _webView.LostFocus -= OnWebViewLostFocus;
        _webView.AttachedToVisualTree -= OnWebViewAttachedToVisualTree;
        _webView.DetachedFromVisualTree -= OnWebViewDetachedFromVisualTree;

        if (_hostWindow != null)
        {
            _hostWindow.Activated -= OnHostWindowActivated;
            _hostWindow = null;
        }

        nint previousHostXid = Interlocked.Exchange(ref _hostXid, 0);
        _ = OnGlibAsync(() => DisposeOnGlib(previousHostXid));
    }

    private void AttachHostWindow()
    {
        DetachHostWindow(restoreFocus: false);
        TopLevel? topLevel = TopLevel.GetTopLevel(_webView);
        IPlatformHandle? hostHandle = topLevel?.TryGetPlatformHandle();
        nint hostXid = hostHandle is { HandleDescriptor: "XID", Handle: not 0 } ? hostHandle.Handle : 0;
        Volatile.Write(ref _hostXid, hostXid);
        if (topLevel is Window window)
        {
            _hostWindow = window;
            window.Activated += OnHostWindowActivated;
        }
    }

    private void DetachHostWindow(bool restoreFocus)
    {
        if (_hostWindow != null)
        {
            _hostWindow.Activated -= OnHostWindowActivated;
            _hostWindow = null;
        }

        nint previousHostXid = Interlocked.Exchange(ref _hostXid, 0);
        if (restoreFocus && previousHostXid != 0)
        {
            _ = OnGlibAsync(() => RestoreHostFocusOnGlib(previousHostXid));
        }
    }

    private void OnWebViewAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_disposed) return;
        AttachHostWindow();
    }

    private void OnWebViewDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_disposed) return;
        DetachHostWindow(restoreFocus: true);
    }

    private void OnWebViewGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_disposed) return;
        if (Volatile.Read(ref _hostXid) == 0) AttachHostWindow();
        _ = OnGlibAsync(FocusWebViewOnGlib);
    }

    private void OnWebViewLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_disposed) return;
        nint hostXid = Volatile.Read(ref _hostXid);
        if (hostXid != 0) _ = OnGlibAsync(() => RestoreHostFocusOnGlib(hostXid));
    }

    private void OnHostWindowActivated(object? sender, EventArgs e)
    {
        // Re-activating the top-level window moves X11 input focus back to it even when Avalonia
        // still considers NativeWebView focused. Hand X11 focus back to WebKitGTK in that case.
        if (_disposed || !_webView.IsFocused) return;
        _ = OnGlibAsync(FocusWebViewOnGlib);
    }

    private unsafe void InitializeOnGlib(bool initiallyFocused)
    {
        if (_disposed || !TryGetXDisplay(out nint gdkDisplay, out nint xDisplay)) return;

        WithErrorTrap(gdkDisplay, xDisplay, () =>
        {
            if (XGetWindowAttributes(xDisplay, _x11Window, out XWindowAttributes attrs) != 0)
            {
                var set = new XSetWindowAttributes
                {
                    DoNotPropagateMask = attrs.DoNotPropagateMask | KeyPropagationMask
                };
                XChangeWindowAttributes(xDisplay, _x11Window, CWDontPropagate, ref set);
            }

            // On WMs without EWMH focus tracking, Avalonia's WindowActivationTrackingHelper falls back
            // to X11 FocusOut events and would treat XSetInputFocus to a child XID as window deactivation.
            _ewmhFocusTracking = SupportsEwmhFocusTracking(xDisplay);
        });

        if (_onBrowserShortcut != null)
        {
            // Blocking core X11 propagation keeps editor/shell shortcuts from seeing page keys, so
            // forward the browser tab's own find/zoom shortcuts directly from GTK key-press-event.
            _keyPressSignal = SignalConnect(
                _gtkWebView,
                "key-press-event",
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnKeyPress,
                GCHandle.ToIntPtr(_selfHandle),
                0,
                0);
        }

        if (_ewmhFocusTracking)
        {
            _buttonPressSignal = SignalConnect(
                _gtkWebView,
                "button-press-event",
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnButtonPress,
                GCHandle.ToIntPtr(_selfHandle),
                0,
                0);

            if (initiallyFocused)
            {
                FocusWebViewOnGlib();
            }
        }
    }

    private void DisposeOnGlib(nint previousHostXid)
    {
        if (_keyPressSignal != 0)
        {
            SignalHandlerDisconnect(_gtkWebView, _keyPressSignal);
            _keyPressSignal = 0;
        }

        if (_buttonPressSignal != 0)
        {
            SignalHandlerDisconnect(_gtkWebView, _buttonPressSignal);
            _buttonPressSignal = 0;
        }

        if (previousHostXid != 0)
        {
            RestoreHostFocusOnGlib(previousHostXid);
        }

        _selfHandle.Free();
        ObjectUnref(_gtkWebView);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnKeyPress(nint widget, nint ev, nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is LinuxWebKitFocus { _disposed: false, _onBrowserShortcut: { } onShortcut } self
                && GdkEventGetKeyval(ev, out uint keyval) != 0
                && GdkEventGetState(ev, out uint state) != 0
                && TryMapBrowserShortcut(keyval, state, out Key key, out KeyModifiers modifiers))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!self._disposed) onShortcut(key, modifiers);
                });
                return 1; // TRUE: handled by the browser tab.
            }
        }
        catch
        {
            // Never allow managed exceptions to escape back into GTK/GLib.
        }

        return 0; // FALSE: let WebKitGTK handle the key normally.
    }

    private static bool TryMapBrowserShortcut(uint keyval, uint state, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;

        if ((state & (GdkControlMask | GdkMod1Mask | GdkSuperMask)) != GdkControlMask)
        {
            return false;
        }

        bool shift = (state & GdkShiftMask) != 0;
        switch (keyval)
        {
            case 0x066 /* GDK_KEY_f */ or 0x046 /* GDK_KEY_F */ when !shift:
                key = Key.F;
                modifiers = KeyModifiers.Control;
                return true;
            case 0x02b /* GDK_KEY_plus */ or 0x03d /* GDK_KEY_equal */ or 0xffab /* GDK_KEY_KP_Add */:
                key = Key.OemPlus;
                modifiers = shift ? KeyModifiers.Control | KeyModifiers.Shift : KeyModifiers.Control;
                return true;
            case 0x02d /* GDK_KEY_minus */ or 0xffad /* GDK_KEY_KP_Subtract */ when !shift:
                key = Key.OemMinus;
                modifiers = KeyModifiers.Control;
                return true;
            case 0x030 /* GDK_KEY_0 */ or 0xffb0 /* GDK_KEY_KP_0 */ when !shift:
                key = Key.D0;
                modifiers = KeyModifiers.Control;
                return true;
            default:
                return false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnButtonPress(nint widget, nint ev, nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is LinuxWebKitFocus { _disposed: false } self)
            {
                self.FocusWebViewOnGlib();
            }
        }
        catch
        {
            // Never allow managed exceptions to escape back into GTK/GLib.
        }

        return 0; // FALSE: let WebKitGTK process the pointer press normally.
    }

    private void FocusWebViewOnGlib()
    {
        if (_disposed || !_ewmhFocusTracking) return;
        nint hostXid = Volatile.Read(ref _hostXid);
        if (hostXid == 0 || !TryGetXDisplay(out nint gdkDisplay, out nint xDisplay)) return;

        WithErrorTrap(gdkDisplay, xDisplay, () =>
        {
            if (XGetWindowAttributes(xDisplay, _x11Window, out XWindowAttributes attrs) == 0
                || attrs.MapState != IsViewable)
            {
                return;
            }

            XGetInputFocus(xDisplay, out nint focused, out _);
            if (IsWindowOrDescendant(xDisplay, focused, _x11Window)) return;
            // Only move focus when our host window (or one of its descendants) already holds it.
            if (!IsWindowOrDescendant(xDisplay, focused, hostXid)) return;

            XSetInputFocus(xDisplay, _x11Window, RevertToParent, CurrentTime);
        });
    }

    private void RestoreHostFocusOnGlib(nint hostXid)
    {
        if (!_ewmhFocusTracking || hostXid == 0 || !TryGetXDisplay(out nint gdkDisplay, out nint xDisplay)) return;

        WithErrorTrap(gdkDisplay, xDisplay, () =>
        {
            XGetInputFocus(xDisplay, out nint focused, out _);
            if (!IsWindowOrDescendant(xDisplay, focused, _x11Window)) return;
            if (XGetWindowAttributes(xDisplay, hostXid, out XWindowAttributes attrs) == 0
                || attrs.MapState != IsViewable)
            {
                return;
            }

            XSetInputFocus(xDisplay, hostXid, RevertToParent, CurrentTime);
        });
    }

    private static bool TryGetXDisplay(out nint gdkDisplay, out nint xDisplay)
    {
        gdkDisplay = GdkDisplayGetDefault();
        if (gdkDisplay == 0 || TypeCheckInstanceIsA(gdkDisplay, GdkX11DisplayGetType()) == 0)
        {
            xDisplay = 0;
            return false;
        }

        xDisplay = GdkX11DisplayGetXDisplay(gdkDisplay);
        return xDisplay != 0;
    }

    private static void WithErrorTrap(nint gdkDisplay, nint xDisplay, Action action)
    {
        GdkX11DisplayErrorTrapPush(gdkDisplay);
        try
        {
            action();
            XSync(xDisplay, 0);
        }
        finally
        {
            GdkX11DisplayErrorTrapPopIgnored(gdkDisplay);
        }
    }

    private static unsafe bool SupportsEwmhFocusTracking(nint xDisplay)
    {
        nint root = XDefaultRootWindow(xDisplay);
        if (root == 0) return false;

        nint netSupported = XInternAtom(xDisplay, "_NET_SUPPORTED", 1);
        if (netSupported == 0) return false;

        nint netWmStateFocused = XInternAtom(xDisplay, "_NET_WM_STATE_FOCUSED", 1);
        nint netActiveWindow = XInternAtom(xDisplay, "_NET_ACTIVE_WINDOW", 1);
        if (netWmStateFocused == 0 && netActiveWindow == 0) return false;

        const nint xaAtom = 4;
        if (XGetWindowProperty(xDisplay, root, netSupported, 0, 4096, 0, xaAtom,
            out nint actualType, out int actualFormat, out nuint count, out _, out nint data) != 0
            || data == 0)
        {
            return false;
        }

        try
        {
            if (actualType != xaAtom || actualFormat != 32) return false;
            // Xlib returns format-32 properties as arrays of C long (pointer-sized on Linux LP64/ILP32).
            var atoms = (nint*)data;
            for (nuint i = 0; i < count; i++)
            {
                nint atom = atoms[i];
                if ((netWmStateFocused != 0 && atom == netWmStateFocused)
                    || (netActiveWindow != 0 && atom == netActiveWindow))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            XFree(data);
        }
    }

    private static bool IsWindowOrDescendant(nint xDisplay, nint candidate, nint ancestor)
    {
        if (candidate is 0 or 1 /* PointerRoot */ || ancestor == 0) return false;

        nint current = candidate;
        for (int depth = 0; depth < 64 && current != 0; depth++)
        {
            if (current == ancestor) return true;
            if (XQueryTree(xDisplay, current, out nint root, out nint parent, out nint children, out _) == 0)
            {
                return false;
            }

            if (children != 0) XFree(children);
            if (parent == 0 || parent == root || parent == current) return false;
            current = parent;
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowAttributes
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int BorderWidth;
        public int Depth;
        public nint Visual;
        public nint Root;
        public int Class;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public nint Colormap;
        public int MapInstalled;
        public int MapState;
        public nint AllEventMasks;
        public nint YourEventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public nint BackgroundPixmap;
        public nuint BackgroundPixel;
        public nint BorderPixmap;
        public nuint BorderPixel;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public nint EventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Colormap;
        public nint Cursor;
    }

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_display_get_default")]
    private static partial nint GdkDisplayGetDefault();

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_x11_display_get_type")]
    private static partial nuint GdkX11DisplayGetType();

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_x11_display_get_xdisplay")]
    private static partial nint GdkX11DisplayGetXDisplay(nint display);

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_x11_display_error_trap_push")]
    private static partial void GdkX11DisplayErrorTrapPush(nint display);

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_x11_display_error_trap_pop_ignored")]
    private static partial void GdkX11DisplayErrorTrapPopIgnored(nint display);

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_event_get_keyval")]
    private static partial int GdkEventGetKeyval(nint ev, out uint keyval);

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_event_get_state")]
    private static partial int GdkEventGetState(nint ev, out uint state);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_type_check_instance_is_a")]
    private static partial int TypeCheckInstanceIsA(nint instance, nuint type);

    [LibraryImport("libX11.so.6")]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(nint display, string atomName, int onlyIfExists);

    [LibraryImport("libX11.so.6")]
    private static partial int XGetWindowProperty(
        nint display,
        nint window,
        nint property,
        nint longOffset,
        nint longLength,
        int delete,
        nint reqType,
        out nint actualTypeReturn,
        out int actualFormatReturn,
        out nuint nitemsReturn,
        out nuint bytesAfterReturn,
        out nint propReturn);

    [LibraryImport("libX11.so.6")]
    private static partial int XGetWindowAttributes(nint display, nint window, out XWindowAttributes attributes);

    [LibraryImport("libX11.so.6")]
    private static partial int XChangeWindowAttributes(nint display, nint window, nuint valueMask, ref XSetWindowAttributes attributes);

    [LibraryImport("libX11.so.6")]
    private static partial int XGetInputFocus(nint display, out nint focusReturn, out int revertToReturn);

    [LibraryImport("libX11.so.6")]
    private static partial int XSetInputFocus(nint display, nint focus, int revertTo, nuint time);

    [LibraryImport("libX11.so.6")]
    private static partial int XQueryTree(
        nint display,
        nint window,
        out nint rootReturn,
        out nint parentReturn,
        out nint childrenReturn,
        out uint nchildrenReturn);

    [LibraryImport("libX11.so.6")]
    private static partial int XFree(nint data);

    [LibraryImport("libX11.so.6")]
    private static partial int XSync(nint display, int discard);
}
