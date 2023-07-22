using Beutl.Graphics;

using OpenTK.Windowing.GraphicsLibraryFramework;

using SkiaSharp;

namespace Beutl.Rendering;

public static unsafe class SharedGRContext
{
    private static Window* s_window;

    public static GRContext? GRContext { get; private set; }

    public static GRContext GetOrCreate()
    {
        if (GRContext == null)
        {
            RenderThread.Dispatcher.VerifyAccess();
            GLFW.Init();
            ThrowGLFWError();

            GLFW.DefaultWindowHints();
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlApi);
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            s_window = GLFW.CreateWindow(1, 1, string.Empty, null, null);

            GLFW.MakeContextCurrent(s_window);
            ThrowGLFWError();

            GRContext = GRContext.CreateGl();
        }

        return GRContext;
    }

    public static void Shutdown()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            GRContext?.Dispose();

            if (s_window != null)
            {
                GLFW.DestroyWindow(s_window);
            }
        });
    }

    private static void ThrowGLFWError()
    {
        var result = GLFW.GetError(out var description);

        if (result is not ErrorCode.NoError)
        {
            throw new GraphicsException(description);
        }
    }
}
