using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Beutl.Helpers;

// The shell does not reveal an auto-hide taskbar over a client area that covers the monitor.
// Leave 2 px free on those edges when maximized, as Chromium and Windows Terminal do.
internal sealed class WindowsAutoHideTaskbar
{
    private const int Gap = 2;
    private const int GwlWndProc = -4;
    private const int GwlStyle = -16;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcDestroy = 0x0082;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const uint SwShowMaximized = 3;
    private const uint MonitorDefaultToNearest = 2;
    private const uint AbmGetAutoHideBarEx = 0x0000000B;
    private const uint AbeLeft = 0;
    private const uint AbeBottom = 3;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private static readonly Dictionary<IntPtr, WindowsAutoHideTaskbar> s_windows = [];
    private readonly IntPtr _previous;
    private readonly Func<IntPtr, (PixelRect Monitor, AutoHideEdges Edges)> _lookup;

    private WindowsAutoHideTaskbar(IntPtr previous, Func<IntPtr, (PixelRect Monitor, AutoHideEdges Edges)> lookup)
    {
        _previous = previous;
        _lookup = lookup;
    }

    public static unsafe WindowsAutoHideTaskbar? TryAttach(Window window,
        Func<IntPtr, (PixelRect Monitor, AutoHideEdges Edges)>? lookup = null)
    {
        window.VerifyAccess();
        if (!OperatingSystem.IsWindows()
            || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: not 0 } handle)
        {
            return null;
        }

        if (s_windows.TryGetValue(handle.Handle, out WindowsAutoHideTaskbar? attached))
            return attached;

        delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> procedure = &WindowProcedure;
        IntPtr previous = SetWindowLongPtrW(handle.Handle, GwlWndProc, (IntPtr)procedure);
        if (previous == IntPtr.Zero)
            return null;

        var helper = new WindowsAutoHideTaskbar(previous, lookup ?? LookupAutoHideEdges);
        s_windows.Add(handle.Handle, helper);
        // A window restored maximized needs its client area recalculated immediately.
        SetWindowPos(handle.Handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
        return helper;
    }

    internal static PixelRect Shrink(PixelRect client, PixelRect monitor, AutoHideEdges edges)
    {
        int left = client.X;
        int top = client.Y;
        int right = client.Right;
        int bottom = client.Bottom;
        if ((edges & AutoHideEdges.Left) != 0 && left <= monitor.X)
            left += Gap;
        if ((edges & AutoHideEdges.Top) != 0 && top <= monitor.Y)
            top += Gap;
        if ((edges & AutoHideEdges.Right) != 0 && right >= monitor.Right)
            right -= Gap;
        if ((edges & AutoHideEdges.Bottom) != 0 && bottom >= monitor.Bottom)
            bottom -= Gap;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    [UnmanagedCallersOnly]
    private static unsafe IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!s_windows.TryGetValue(hwnd, out WindowsAutoHideTaskbar? helper))
            return DefWindowProcW(hwnd, message, wParam, lParam);

        if (message == WmNcDestroy)
            s_windows.Remove(hwnd);

        IntPtr result = CallWindowProcW(helper._previous, hwnd, message, wParam, lParam);
        if (message == WmNcCalcSize && wParam != IntPtr.Zero)
        {
            var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
            // Full screen removes both frame styles and must continue covering the whole monitor.
            if (GetWindowPlacement(hwnd, ref placement) && placement.ShowCommand == SwShowMaximized
                && (GetWindowLongW(hwnd, GwlStyle) & (WsCaption | WsThickFrame)) != 0)
            {
                (PixelRect monitor, AutoHideEdges edges) = helper._lookup(hwnd);
                ref NativeRect client = ref ((NcCalcSizeParams*)lParam)->NewClient;
                PixelRect shrunk = Shrink(client.ToPixelRect(), monitor, edges);
                client = new NativeRect { Left = shrunk.X, Top = shrunk.Y, Right = shrunk.Right, Bottom = shrunk.Bottom };
            }
        }

        return result;
    }

    private static (PixelRect Monitor, AutoHideEdges Edges) LookupAutoHideEdges(IntPtr hwnd)
    {
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref monitor))
            return (default, AutoHideEdges.None);

        AutoHideEdges edges = AutoHideEdges.None;
        var appBar = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        for (uint edge = AbeLeft; edge <= AbeBottom; edge++)
        {
            appBar.Edge = edge;
            appBar.Rect = monitor.Monitor;
            if (SHAppBarMessage(AbmGetAutoHideBarEx, ref appBar) != UIntPtr.Zero)
                edges |= (AutoHideEdges)(1 << (int)edge);
        }

        return (monitor.Monitor.ToPixelRect(), edges);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams
    {
        public NativeRect NewClient;
        public NativeRect OldWindow;
        public NativeRect OldClient;
        public IntPtr WindowPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr Param;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

[Flags]
internal enum AutoHideEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}
