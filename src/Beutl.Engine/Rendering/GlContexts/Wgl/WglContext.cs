using SkiaSharp;

namespace Beutl.Rendering.GlContexts;

//https://github.com/mono/SkiaSharp/blob/main/tests/Tests/SkiaSharp/GlContexts/Wgl/WglContext.cs
internal class WglContext : GlContext
{
    private static readonly object s_lock = new object();

    private static readonly Win32Window s_window = new("WglContext");

    private nint pbufferHandle;
    private nint pbufferDeviceContextHandle;
    private nint pbufferGlContextHandle;

    public WglContext()
    {
        // 使うことが分かっているので先にNullチェック
        if (Wgl.wglChoosePixelFormatARB == null)
            throw new Exception("Not found 'wglChoosePixelFormatARB'.");

        if (Wgl.wglCreatePbufferARB == null)
            throw new Exception("Not found 'wglCreatePbufferARB'.");

        if (Wgl.wglGetPbufferDCARB == null)
            throw new Exception("Not found 'wglGetPbufferDCARB'.");

        if (!Wgl.HasExtension(s_window.DeviceContextHandle, "WGL_ARB_pixel_format") ||
            !Wgl.HasExtension(s_window.DeviceContextHandle, "WGL_ARB_pbuffer"))
        {
            throw new Exception("DC does not have extensions.");
        }

        int[] iAttrs = new int[]
        {
            Wgl.WGL_ACCELERATION_ARB, Wgl.WGL_FULL_ACCELERATION_ARB,
            Wgl.WGL_DRAW_TO_WINDOW_ARB, Wgl.TRUE,
            //Wgl.WGL_DOUBLE_BUFFER_ARB, (doubleBuffered ? TRUE : FALSE),
            Wgl.WGL_SUPPORT_OPENGL_ARB, Wgl.TRUE,
            Wgl.WGL_RED_BITS_ARB, 8,
            Wgl.WGL_GREEN_BITS_ARB, 8,
            Wgl.WGL_BLUE_BITS_ARB, 8,
            Wgl.WGL_ALPHA_BITS_ARB, 8,
            Wgl.WGL_STENCIL_BITS_ARB, 8,
            Wgl.NONE, Wgl.NONE
        };
        int[] piFormats = new int[1];
        uint nFormats;
        lock (s_lock)
        {
            // HACK: This call seems to cause deadlocks on some systems.
            Wgl.wglChoosePixelFormatARB(s_window.DeviceContextHandle, iAttrs, null, (uint)piFormats.Length, piFormats, out nFormats);
        }
        if (nFormats == 0)
        {
            Destroy();
            throw new Exception("Could not get pixel formats.");
        }

        pbufferHandle = Wgl.wglCreatePbufferARB(s_window.DeviceContextHandle, piFormats[0], 1, 1, null);
        if (pbufferHandle == nint.Zero)
        {
            Destroy();
            throw new Exception("Could not create Pbuffer.");
        }

        pbufferDeviceContextHandle = Wgl.wglGetPbufferDCARB(pbufferHandle);
        if (pbufferDeviceContextHandle == nint.Zero)
        {
            Destroy();
            throw new Exception("Could not get Pbuffer DC.");
        }

        nint prevDC = Wgl.wglGetCurrentDC();
        nint prevGLRC = Wgl.wglGetCurrentContext();

        pbufferGlContextHandle = Wgl.wglCreateContext(pbufferDeviceContextHandle);

        Wgl.wglMakeCurrent(prevDC, prevGLRC);

        if (pbufferGlContextHandle == nint.Zero)
        {
            Destroy();
            throw new Exception("Could not creeate Pbuffer GL context.");
        }
    }

    public override void MakeCurrent()
    {
        if (!Wgl.wglMakeCurrent(pbufferDeviceContextHandle, pbufferGlContextHandle))
        {
            Destroy();
            throw new Exception("Could not set the context.");
        }
    }

    public override void SwapBuffers()
    {
        if (!Gdi32.SwapBuffers(pbufferDeviceContextHandle))
        {
            Destroy();
            throw new Exception("Could not complete SwapBuffers.");
        }
    }

    public override void Destroy()
    {
        if (pbufferGlContextHandle != nint.Zero)
        {
            Wgl.wglDeleteContext(pbufferGlContextHandle);
            pbufferGlContextHandle = nint.Zero;
        }

        if (pbufferHandle != nint.Zero)
        {
            if (pbufferDeviceContextHandle != nint.Zero)
            {
                if (!Wgl.HasExtension(pbufferDeviceContextHandle, "WGL_ARB_pbuffer"))
                {
                    // ASSERT
                }

                Wgl.wglReleasePbufferDCARB?.Invoke(pbufferHandle, pbufferDeviceContextHandle);
                pbufferDeviceContextHandle = nint.Zero;
            }

            Wgl.wglDestroyPbufferARB?.Invoke(pbufferHandle);
            pbufferHandle = nint.Zero;
        }
    }

    public override GRGlTextureInfo CreateTexture(SKSizeI textureSize)
    {
        var textures = new uint[1];
        Wgl.glGenTextures(textures.Length, textures);
        var textureId = textures[0];

        Wgl.glBindTexture(Wgl.GL_TEXTURE_2D, textureId);
        Wgl.glTexImage2D(Wgl.GL_TEXTURE_2D, 0, Wgl.GL_RGBA, textureSize.Width, textureSize.Height, 0, Wgl.GL_RGBA, Wgl.GL_UNSIGNED_BYTE, nint.Zero);
        Wgl.glBindTexture(Wgl.GL_TEXTURE_2D, 0);

        return new GRGlTextureInfo
        {
            Id = textureId,
            Target = Wgl.GL_TEXTURE_2D,
            Format = Wgl.GL_RGBA8
        };
    }

    public override void DestroyTexture(uint texture)
    {
        Wgl.glDeleteTextures(1, new[] { texture });
    }
}
