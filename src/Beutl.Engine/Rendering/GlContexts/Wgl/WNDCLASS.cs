using System.Runtime.InteropServices;

namespace Beutl.Rendering.GlContexts;

internal delegate nint WNDPROC(nint hWnd, uint msg, nint wParam, nint lParam);

[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASS
{
    public uint style;
    [MarshalAs(UnmanagedType.FunctionPtr)]
    public WNDPROC lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    [MarshalAs(UnmanagedType.LPTStr)]
    public string? lpszMenuName;
    [MarshalAs(UnmanagedType.LPTStr)]
    public string lpszClassName;
}
