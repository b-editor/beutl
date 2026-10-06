using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Beutl.Controls;

public partial class HdrBitmapView
{
    #region Platform-specific native window creation

    // Windows
    private static IPlatformHandle CreateWindowsControl(IPlatformHandle parent)
    {
        var hwnd = CreateWindowExW(
            0x00000000, // dwExStyle
            "Static", // lpClassName
            "", // lpWindowName
            0x40000000 | 0x10000000, // WS_CHILD | WS_VISIBLE
            0, 0, 1, 1,
            parent.Handle,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create child HWND");

        return new PlatformHandle(hwnd, "HWND");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // macOS
    private static IPlatformHandle CreateMacOSControl(IPlatformHandle parent)
    {
        // [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 1, 1)]
        var nsViewClass = objc_getClass("NSView");
        var allocSel = sel_getUid("alloc");
        var initWithFrameSel = sel_getUid("initWithFrame:");

        var nsView = objc_msgSend_IntPtr(nsViewClass, allocSel);
        nsView = objc_msgSend_NSRect(nsView, initWithFrameSel, 0, 0, 1, 1);

        if (nsView == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create NSView");

        return new PlatformHandle(nsView, "NSView");
    }

    private static void ReleaseMacOSView(IntPtr nsView)
    {
        if (nsView != IntPtr.Zero)
        {
            var releaseSel = sel_getUid("release");
            objc_msgSend_void(nsView, releaseSel);
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    // initWithFrame: takes an NSRect (CGRect) which is { x, y, width, height } as doubles
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_NSRect(
        IntPtr receiver, IntPtr selector,
        double x, double y, double width, double height);

    // Linux
    private static IPlatformHandle CreateLinuxControl(IPlatformHandle parent)
    {
        // For X11, we return the parent handle and let the surface helper handle it
        // In a full implementation, we'd create a child X window
        return parent;
    }

    #endregion

    private sealed class PlatformHandle(IntPtr handle, string descriptor) : IPlatformHandle
    {
        public IntPtr Handle { get; } = handle;
        public string HandleDescriptor { get; } = descriptor;
    }
}
