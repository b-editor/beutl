using System.Runtime.InteropServices;

#pragma warning disable SYSLIB1054
#pragma warning disable IDE1006

namespace Beutl.Rendering.GlContexts;

internal class Xlib
{
    private const string libX11 = "libX11.so.6";

    public const int None = 0;
    public const int True = 1;
    public const int False = 0;

    [DllImport(libX11, CharSet = CharSet.Unicode)]
    public static extern IntPtr XOpenDisplay(string? display_name);

    [DllImport(libX11)]
    public static extern int XFree(IntPtr data);

    [DllImport(libX11)]
    public static extern int XDefaultScreen(IntPtr display);

    [DllImport(libX11)]
    public static extern IntPtr XRootWindow(IntPtr display, int screen);

    [DllImport(libX11)]
    public static extern IntPtr XCreatePixmap(IntPtr display, IntPtr d, uint width, uint height, uint depth);

    [DllImport(libX11)]
    public static extern IntPtr XFreePixmap(IntPtr display, IntPtr pixmap);
}
