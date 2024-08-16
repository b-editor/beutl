using System.Runtime.Versioning;
using Beutl.Graphics.Rendering.GlContexts;
using Microsoft.Extensions.Logging;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public unsafe class SharedGRContext
{
    private static readonly ILogger<SharedGRContext> s_logger = BeutlApplication.Current.LoggerFactory.CreateLogger<SharedGRContext>();

    private static Window* s_window;
    private static GlContext? s_glContext;
    private static bool s_failedToInitialize;

    public static GRContext? GRContext { get; private set; }

    public static string? Version { get; private set; }

    public static GRContext? GetOrCreate()
    {
        if (s_failedToInitialize)
            return null;

        if (GRContext == null)
        {
            RenderThread.Dispatcher.VerifyAccess();

            // プラットフォームのGLContextの初期化に失敗した場合、GLFWを使う
            if (!TryInitializePlatformGl())
            {
                if (!TryInitializeGLFW())
                {
                    s_failedToInitialize = true;
                    return null;
                }
            }

            GRContext = GRContext.CreateGl();
        }

        return GRContext;
    }

    private static bool TryInitializePlatformGl()
    {
        if (OperatingSystem.IsWindows())
        {
            return TryInitializeWgl();
        }
        else if (OperatingSystem.IsLinux())
        {
            return TryInitializeGlx();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return TryInitializeCgl();
        }
        else
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryInitializeWgl()
    {
        try
        {
            s_glContext = new WglContext();
            s_glContext.MakeCurrent();

            if (Wgl.VersionString != null)
            {
                Version = $"Wgl {Wgl.VersionString}";
            }
            else
            {
                Version = "Wgl";
            }

            s_logger.LogInformation("Using {Version}.", Version);
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Initialization of glContext using wgl failed.");
            return false;
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool TryInitializeGlx()
    {
        try
        {
            s_glContext = new GlxContext();
            s_glContext.MakeCurrent();

            if (Glx.glXQueryVersion(((GlxContext)s_glContext).fDisplay, out int major, out int minor))
            {
                Version = $"Glx {major}.{minor}";
            }
            else
            {
                Version = "Glx";
            }

            s_logger.LogInformation("Using {Version}.", Version);
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Initialization of glContext using glx failed.");
            return false;
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool TryInitializeCgl()
    {
        try
        {
            s_glContext = new CglContext();
            s_glContext.MakeCurrent();

            Cgl.CGLGetVersion(out int major, out int minor);
            Version = $"CGL {major}.{minor}";

            s_logger.LogInformation("Using {Version}.", Version);
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Initialization of glContext using cgl failed.");
            return false;
        }
    }

    private static bool TryInitializeGLFW()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            GLFW.Init();
            if (GLFW.GetError(out string error) is not ErrorCode.NoError)
                throw new Exception($"GLFW error: {error}");

            GLFW.DefaultWindowHints();
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlApi);
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            s_window = GLFW.CreateWindow(1, 1, string.Empty, null, null);

            GLFW.MakeContextCurrent(s_window);
            if (GLFW.GetError(out error) is not ErrorCode.NoError)
            {
                GLFW.DestroyWindow(s_window);
                s_window = null;

                throw new Exception($"GLFW error: {error}");
            }

            Version = $"GLFW {GLFW.GetVersionString()}";
            s_logger.LogInformation("Using {Version}.", Version);
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Initialization of glContext using GLFW failed.");
            return false;
        }
    }

    public static void Shutdown()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            GRContext?.Dispose();

            if (s_glContext != null)
            {
                s_glContext.Destroy();
                s_glContext = null;
            }

            if (s_window != null)
            {
                GLFW.DestroyWindow(s_window);
            }
        });
    }
}
