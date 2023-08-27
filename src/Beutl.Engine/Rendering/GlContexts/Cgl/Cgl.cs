using System.Runtime.InteropServices;

#pragma warning disable SYSLIB1054
#pragma warning disable IDE1006

namespace Beutl.Rendering.GlContexts;

internal class Cgl
{
    private const string libGL = "/System/Library/Frameworks/OpenGL.framework/Versions/A/OpenGL";

    public const int GL_TEXTURE_2D = 0x0DE1;
    public const int GL_UNSIGNED_BYTE = 0x1401;
    public const int GL_RGBA = 0x1908;
    public const int GL_RGBA8 = 0x8058;

    [DllImport(libGL)]
    public static extern void CGLGetVersion(out int majorvers, out int minorvers);

    [DllImport(libGL)]
    public static extern CGLError CGLChoosePixelFormat([In] CGLPixelFormatAttribute[] attribs, out IntPtr pix, out int npix);

    [DllImport(libGL)]
    public static extern CGLError CGLCreateContext(IntPtr pix, IntPtr share, out IntPtr ctx);

    [DllImport(libGL)]
    public static extern CGLError CGLReleasePixelFormat(IntPtr pix);

    [DllImport(libGL)]
    public static extern CGLError CGLSetCurrentContext(IntPtr ctx);

    [DllImport(libGL)]
    public static extern void CGLReleaseContext(IntPtr ctx);

    [DllImport(libGL)]
    public static extern CGLError CGLFlushDrawable(IntPtr ctx);

    [DllImport(libGL)]
    public static extern void glGenTextures(int n, uint[] textures);

    [DllImport(libGL)]
    public static extern void glDeleteTextures(int n, uint[] textures);

    [DllImport(libGL)]
    public static extern void glBindTexture(uint target, uint texture);

    [DllImport(libGL)]
    public static extern void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, IntPtr pixels);
}
