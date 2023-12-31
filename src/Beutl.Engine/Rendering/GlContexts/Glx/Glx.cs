using System.Runtime.InteropServices;

#pragma warning disable SYSLIB1054
#pragma warning disable IDE1006

namespace Beutl.Rendering.GlContexts;

internal class Glx
{
    private const string libGL = "libGL.so.1";

    public const int GL_TEXTURE_2D = 0x0DE1;
    public const int GL_UNSIGNED_BYTE = 0x1401;
    public const int GL_RGBA = 0x1908;
    public const int GL_RGBA8 = 0x8058;

    public const int GLX_USE_GL = 1;
    public const int GLX_BUFFER_SIZE = 2;
    public const int GLX_LEVEL = 3;
    public const int GLX_RGBA = 4;
    public const int GLX_DOUBLEBUFFER = 5;
    public const int GLX_STEREO = 6;
    public const int GLX_AUX_BUFFERS = 7;
    public const int GLX_RED_SIZE = 8;
    public const int GLX_GREEN_SIZE = 9;
    public const int GLX_BLUE_SIZE = 10;
    public const int GLX_ALPHA_SIZE = 11;
    public const int GLX_DEPTH_SIZE = 12;
    public const int GLX_STENCIL_SIZE = 13;
    public const int GLX_ACCUM_RED_SIZE = 14;
    public const int GLX_ACCUM_GREEN_SIZE = 15;
    public const int GLX_ACCUM_BLUE_SIZE = 16;
    public const int GLX_ACCUM_ALPHA_SIZE = 17;

    public const int GLX_DRAWABLE_TYPE = 0x8010;
    public const int GLX_RENDER_TYPE = 0x8011;
    public const int GLX_X_RENDERABLE = 0x8012;
    public const int GLX_RGBA_TYPE = 0x8014;
    public const int GLX_COLOR_INDEX_TYPE = 0x8015;

    public const int GLX_PIXMAP_BIT = 0x00000002;

    public const int GLX_RGBA_BIT = 0x00000001;

    public const int GLX_SAMPLE_BUFFERS = 0x186a0;
    public const int GLX_SAMPLES = 0x186a1;

    public const int GLX_CONTEXT_DEBUG_BIT_ARB = 0x00000001;
    public const int GLX_CONTEXT_FORWARD_COMPATIBLE_BIT_ARB = 0x00000002;
    public const int GLX_CONTEXT_MAJOR_VERSION_ARB = 0x2091;
    public const int GLX_CONTEXT_MINOR_VERSION_ARB = 0x2092;
    public const int GLX_CONTEXT_FLAGS_ARB = 0x2094;

    public const int GLX_CONTEXT_CORE_PROFILE_BIT_ARB = 0x00000001;
    public const int GLX_CONTEXT_COMPATIBILITY_PROFILE_BIT_ARB = 0x00000002;
    public const int GLX_CONTEXT_PROFILE_MASK_ARB = 0x9126;

    static Glx()
    {
        nint ptr = glXGetProcAddressARB("glXCreateContextAttribsARB");
        if (ptr != IntPtr.Zero)
        {
            glXCreateContextAttribsARB = (glXCreateContextAttribsARBDelegate)Marshal.GetDelegateForFunctionPointer(ptr, typeof(glXCreateContextAttribsARBDelegate));
        }
    }

    public static readonly glXCreateContextAttribsARBDelegate? glXCreateContextAttribsARB;

    [DllImport(libGL)]
    public static extern bool glXQueryVersion(IntPtr dpy, out int maj, out int min);

    [DllImport(libGL)]
    public static extern IntPtr glXChooseFBConfig(IntPtr dpy, int screen, [In] int[] attribList, out int nitems);

    public static IntPtr[] ChooseFBConfig(IntPtr dpy, int screen, int[] attribList)
    {
        nint fbcArrayPtr = glXChooseFBConfig(dpy, screen, attribList, out int nitems);

        nint[] fbcArray = new IntPtr[nitems];
        Marshal.Copy(fbcArrayPtr, fbcArray, 0, nitems);

        _ = Xlib.XFree(fbcArrayPtr);

        return fbcArray;
    }

    [DllImport(libGL)]
    public static extern IntPtr glXGetVisualFromFBConfig(IntPtr dpy, IntPtr config);

    public static XVisualInfo GetVisualFromFBConfig(IntPtr dpy, IntPtr config)
    {
        nint visualPtr = glXGetVisualFromFBConfig(dpy, config);
        if (visualPtr == IntPtr.Zero)
        {
            throw new Exception("Failed to retrieve visual from framebuffer config.");
        }

        XVisualInfo visual = Marshal.PtrToStructure<XVisualInfo>(visualPtr);

        _ = Xlib.XFree(visualPtr);

        return visual;
    }

    [DllImport(libGL)]
    public static extern bool glXMakeCurrent(IntPtr dpy, IntPtr drawable, IntPtr ctx);

    [DllImport(libGL)]
    public static extern bool glXSwapBuffers(IntPtr dpy, IntPtr drawable);

    [DllImport(libGL)]
    public static extern bool glXIsDirect(IntPtr dpy, IntPtr ctx);

    [DllImport(libGL)]
    public static extern int glXGetFBConfigAttrib(IntPtr dpy, IntPtr config, int attribute, out int value);

    [DllImport(libGL)]
    public static extern IntPtr glXCreateGLXPixmap(IntPtr dpy, ref XVisualInfo visual, IntPtr pixmap);

    [DllImport(libGL)]
    public static extern void glXDestroyGLXPixmap(IntPtr dpy, IntPtr pixmap);

    [DllImport(libGL)]
    public static extern void glXDestroyContext(IntPtr dpy, IntPtr ctx);

    [DllImport(libGL)]
    public static extern IntPtr glXQueryExtensionsString(IntPtr dpy, int screen);

    public static string? QueryExtensionsString(IntPtr dpy, int screen)
    {
        return Marshal.PtrToStringAnsi(glXQueryExtensionsString(dpy, screen));
    }

    public static string[] QueryExtensions(IntPtr dpy, int screen)
    {
        string? str = QueryExtensionsString(dpy, screen);
        if (string.IsNullOrEmpty(str))
        {
            return [];
        }

        return str.Split(' ');
    }

    [DllImport(libGL, CharSet = CharSet.Unicode)]
    public static extern IntPtr glXGetProcAddressARB(string procname);

    [DllImport(libGL)]
    public static extern IntPtr glXCreateNewContext(IntPtr dpy, IntPtr config, int renderType, IntPtr shareList, int direct);

    [DllImport(libGL)]
    public static extern void glGenTextures(int n, uint[] textures);

    [DllImport(libGL)]
    public static extern void glDeleteTextures(int n, uint[] textures);

    [DllImport(libGL)]
    public static extern void glBindTexture(uint target, uint texture);

    [DllImport(libGL)]
    public static extern void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, IntPtr pixels);
}

internal delegate IntPtr glXCreateContextAttribsARBDelegate(IntPtr dpy, IntPtr config, IntPtr share_context, int direct, int[] attrib_list);
