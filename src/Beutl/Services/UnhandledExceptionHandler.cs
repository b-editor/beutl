using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Rendering;

using Serilog;

namespace Beutl.Services;

public static class UnhandledExceptionHandler
{
    private const string LastUnhandledExeptionFileName = "last-unhandled-exeption";
    private static bool s_exited;

    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    // 最後に実行されたとき、例外が発生して終了したかどうか。
    public static bool LastExecutionExceptionWasThrown()
    {
        return File.Exists(Path.Combine(Helper.AppRoot, LastUnhandledExeptionFileName));
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "An unhandled exception occurred. (IsTerminating: {IsTerminating})", e.IsTerminating);
                Telemetry.Exception(ex, true);
                SaveException(ex);

                //var stack = new StackTrace();
                //var fr = stack.GetFrames();
                //Todo: スタックトレースからどこの拡張機能が例外を投げたかを追跡したい
            }

            PrivateExit();

            string exePath = Path.Combine(
                AppContext.BaseDirectory,
                OperatingSystem.IsWindows() ? "Beutl.ExceptionHandler.exe" : "Beutl.ExceptionHandler");
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static void SaveException(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(Helper.AppRoot, LastUnhandledExeptionFileName), ex.ToString());
        }
        catch
        {
        }
    }

    private static void PrivateExit()
    {
        if (!s_exited)
        {
            GlobalConfiguration.Instance.Save(GlobalConfiguration.DefaultFilePath);
            BeutlApplication.Current.LoggerFactory.Dispose();

            SharedGPUContext.Shutdown();
            SharedGRContext.Shutdown();
            RenderThread.Dispatcher.Stop();

            s_exited = true;
        }
    }

    public static void Exit()
    {
        if (!s_exited)
        {
            PrivateExit();

            string path = Path.Combine(Helper.AppRoot, LastUnhandledExeptionFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
