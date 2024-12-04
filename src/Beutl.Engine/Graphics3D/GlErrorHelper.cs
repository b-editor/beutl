using Beutl.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Beutl.Graphics3D;

internal static class GlErrorHelper
{
    public static void CheckGlfwError()
    {
        var result = GLFW.GetError(out string? description);

        if (result is not OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode.NoError)
        {
            throw new GraphicsException(description);
        }
    }

    public static void CheckGlError()
    {
        var result = GL.GetError();

        if (result is not OpenTK.Graphics.OpenGL.ErrorCode.NoError)
        {
            throw new GraphicsException(result.ToString());
        }
    }

    public static void CheckFramebufferError()
    {
        var result = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        if (result is not FramebufferStatus.FramebufferComplete)
        {
            throw new GraphicsException(result.ToString());
        }
    }
}
