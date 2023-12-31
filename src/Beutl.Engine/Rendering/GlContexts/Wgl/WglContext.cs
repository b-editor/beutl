using SkiaSharp;

namespace Beutl.Rendering.GlContexts;

//https://github.com/mono/SkiaSharp/blob/main/tests/Tests/SkiaSharp/GlContexts/Wgl/WglContext.cs
internal class WglContext : GlContext
{
    private static readonly object s_lock = new();

    private static readonly Win32Window s_window = new("WglContext");

    private nint _pbufferHandle;
    private nint _pbufferDeviceContextHandle;
    private nint _pbufferGlContextHandle;

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

        int[] iAttrs =
        [
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
        ];
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

        _pbufferHandle = Wgl.wglCreatePbufferARB(s_window.DeviceContextHandle, piFormats[0], 1, 1, null);
        if (_pbufferHandle == nint.Zero)
        {
            Destroy();
            throw new Exception("Could not create Pbuffer.");
        }

        _pbufferDeviceContextHandle = Wgl.wglGetPbufferDCARB(_pbufferHandle);
        if (_pbufferDeviceContextHandle == nint.Zero)
        {
            Destroy();
            throw new Exception("Could not get Pbuffer DC.");
        }

        nint prevDC = Wgl.wglGetCurrentDC();
        nint prevGLRC = Wgl.wglGetCurrentContext();

        _pbufferGlContextHandle = Wgl.wglCreateContext(_pbufferDeviceContextHandle);

        Wgl.wglMakeCurrent(prevDC, prevGLRC);

        if (_pbufferGlContextHandle == nint.Zero)
        {
            Destroy();
            throw new Exception("Could not creeate Pbuffer GL context.");
        }
    }

    public override void MakeCurrent()
    {
        if (!Wgl.wglMakeCurrent(_pbufferDeviceContextHandle, _pbufferGlContextHandle))
        {
            Destroy();
            throw new Exception("Could not set the context.");
        }
    }

    public override void SwapBuffers()
    {
        if (!Gdi32.SwapBuffers(_pbufferDeviceContextHandle))
        {
            Destroy();
            throw new Exception("Could not complete SwapBuffers.");
        }
    }

    public override void Destroy()
    {
        if (_pbufferGlContextHandle != nint.Zero)
        {
            Wgl.wglDeleteContext(_pbufferGlContextHandle);
            _pbufferGlContextHandle = nint.Zero;
        }

        if (_pbufferHandle != nint.Zero)
        {
            if (_pbufferDeviceContextHandle != nint.Zero)
            {
                if (!Wgl.HasExtension(_pbufferDeviceContextHandle, "WGL_ARB_pbuffer"))
                {
                    // ASSERT
                }

                Wgl.wglReleasePbufferDCARB?.Invoke(_pbufferHandle, _pbufferDeviceContextHandle);
                _pbufferDeviceContextHandle = nint.Zero;
            }

            Wgl.wglDestroyPbufferARB?.Invoke(_pbufferHandle);
            _pbufferHandle = nint.Zero;
        }
    }

    public override GRGlTextureInfo CreateTexture(SKSizeI textureSize)
    {
        uint[] textures = new uint[1];
        Wgl.glGenTextures(textures.Length, textures);
        uint textureId = textures[0];

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
        Wgl.glDeleteTextures(1, [texture]);
    }
}
