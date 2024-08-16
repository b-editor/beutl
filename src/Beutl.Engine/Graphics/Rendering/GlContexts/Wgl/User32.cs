using System.Runtime.InteropServices;
using System.Text;

namespace Beutl.Graphics.Rendering.GlContexts;

#pragma warning disable SYSLIB1054
#pragma warning disable IDE1006

internal static class User32
{
    private const string user32 = "user32.dll";

    public const uint IDC_ARROW = 32512;

    public const uint IDI_APPLICATION = 32512;
    public const uint IDI_WINLOGO = 32517;

    public const int SW_HIDE = 0;

    public const uint CS_VREDRAW = 0x1;
    public const uint CS_HREDRAW = 0x2;
    public const uint CS_OWNDC = 0x20;

    public const uint WS_EX_CLIENTEDGE = 0x00000200;

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern ushort UnregisterClass([MarshalAs(UnmanagedType.LPTStr)] string lpClassName, nint hInstance);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern nint LoadCursor(nint hInstance, int lpCursorName);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi)]
    public static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern nint CreateWindowEx(uint dwExStyle, [MarshalAs(UnmanagedType.LPTStr)] string lpClassName, [MarshalAs(UnmanagedType.LPTStr)] string lpWindowName, WindowStyles dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    public static nint CreateWindow(string lpClassName, string lpWindowName, WindowStyles dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam)
    {
        return CreateWindowEx(0, lpClassName, lpWindowName, dwStyle, x, y, nWidth, nHeight, hWndParent, hMenu, hInstance, lpParam);
    }

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern nint GetDC(nint hWnd);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseDC(nint hWnd, nint hDC);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hWnd);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AdjustWindowRectEx(ref RECT lpRect, WindowStyles dwStyle, bool bMenu, uint dwExStyle);

    [DllImport(user32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, uint nCmdShow);
}
