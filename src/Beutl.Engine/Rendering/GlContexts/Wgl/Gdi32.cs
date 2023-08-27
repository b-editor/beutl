using System.Runtime.InteropServices;

namespace Beutl.Rendering.GlContexts;

#pragma warning disable SYSLIB1054
#pragma warning disable IDE1006

internal static class Gdi32
{
    private const string gdi32 = "gdi32.dll";

    public const byte PFD_TYPE_RGBA = 0;

    public const byte PFD_MAIN_PLANE = 0;

    public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    public const uint PFD_SUPPORT_OPENGL = 0x00000020;

    [DllImport(gdi32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetPixelFormat(nint hdc, int iPixelFormat, [In] ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport(gdi32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern int ChoosePixelFormat(nint hdc, [In] ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport(gdi32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SwapBuffers(nint hdc);
}
