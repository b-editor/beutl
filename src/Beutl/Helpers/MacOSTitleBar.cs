using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Beutl.Helpers;

// Avalonia 12 removed OSXThickTitleBar. Restore its empty NSToolbar for the main window.
// https://github.com/AvaloniaUI/Avalonia/issues/21119
internal sealed partial class MacOSTitleBar : IDisposable
{
    private readonly Window _window;
    private readonly INativeToolbar _toolbar;
    private bool _updateQueued;
    private bool _disposed;

    internal MacOSTitleBar(Window window, INativeToolbar toolbar)
    {
        _window = window;
        _toolbar = toolbar;
        _window.PropertyChanged += OnPropertyChanged;
        _window.Closed += OnClosed;
        Update();
    }

    public static MacOSTitleBar? TryAttach(Window window)
    {
        window.VerifyAccess();
        if (!OperatingSystem.IsMacOS()
            || !window.ExtendClientAreaToDecorationsHint
            || window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow", Handle: not 0 } handle)
        {
            return null;
        }

        NativeToolbar? toolbar = NativeToolbar.TryCreate(handle.Handle);
        return toolbar is null ? null : new MacOSTitleBar(window, toolbar);
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty || _updateQueued || _disposed)
            return;

        // Avalonia updates the native titlebar during fullscreen transitions. Apply afterward,
        // using the latest state if more than one transition arrives before the callback runs.
        _updateQueued = true;
        _window.Dispatcher.Post(() =>
        {
            _updateQueued = false;
            if (!_disposed)
                Update();
        }, DispatcherPriority.Background);
    }

    private void Update() => _toolbar.Update(_window.WindowState == WindowState.FullScreen);

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        _window.VerifyAccess();
        if (_disposed)
            return;

        _disposed = true;
        _window.PropertyChanged -= OnPropertyChanged;
        _window.Closed -= OnClosed;
        _toolbar.Dispose();
    }

    internal interface INativeToolbar : IDisposable
    {
        void Update(bool fullScreen);
    }

    [SupportedOSPlatform("macos")]
    private sealed partial class NativeToolbar(nint window, nint toolbar) : INativeToolbar
    {
        private const string LibObjC = "/usr/lib/libobjc.A.dylib";
        private readonly nint _window = window;
        private nint _toolbar = toolbar;

        public static NativeToolbar? TryCreate(nint window)
        {
            // 'new' returns an owned reference. NSWindow also retains the toolbar while attached.
            nint toolbar = Send(objc_getClass("NSToolbar"), sel_registerName("new"));
            if (toolbar == 0)
                return null;

            SendBool(toolbar, sel_registerName("setShowsBaselineSeparator:"), false);
            return new NativeToolbar(window, toolbar);
        }

        public void Update(bool fullScreen)
        {
            // An attached toolbar creates an opaque strip in fullscreen, so remove it there
            // and reuse it on exit. Keep AppKit's standard fullscreen button auto-hiding.
            SendPointer(_window, sel_registerName("setToolbar:"), fullScreen ? 0 : _toolbar);
            SendBool(_window, sel_registerName("setTitlebarAppearsTransparent:"), true);
            SendPointer(_window, sel_registerName("setTitleVisibility:"), 1); // NSWindowTitleHidden
        }

        public void Dispose()
        {
            // Closed may run after NSWindow was destroyed: only release our owned toolbar.
            // Never send a message to the cached NSWindow pointer during teardown.
            if (_toolbar != 0)
            {
                SendVoid(_toolbar, sel_registerName("release"));
                _toolbar = 0;
            }
        }

        [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint objc_getClass(string name);

        [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint sel_registerName(string name);

        [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static partial nint Send(nint receiver, nint selector);

        [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static partial void SendVoid(nint receiver, nint selector);

        [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static partial void SendPointer(nint receiver, nint selector, nint value);

        [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static partial void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);
    }
}
