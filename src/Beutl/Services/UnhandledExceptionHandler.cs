using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Rendering;

using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public static class UnhandledExceptionHandler
{
    private const string LastUnhandledExeptionFileName = "last-unhandled-exeption";
    private static bool s_exited;
    private static ILogger? s_logger;

    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        s_logger = Log.LoggerFactory.CreateLogger(typeof(UnhandledExceptionHandler));
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
                s_logger?.LogCritical(ex, "An unhandled exception occurred. (IsTerminating: {IsTerminating})", e.IsTerminating);
                SaveException(ex);

                //var stack = new StackTrace();
                //var fr = stack.GetFrames();
                //Todo: スタックトレースからどこの拡張機能が例外を投げたかを追跡したい
            }

            PrivateExit();

            string exePath = Path.Combine(
                AppContext.BaseDirectory,
                "Beutl.ExceptionHandler");

            var startInfo = new ProcessStartInfo()
            {
                UseShellExecute = true
            };
            DotNetProcess.Configure(startInfo, exePath);
            Process.Start(startInfo);
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

            SharedGPUContext.Shutdown();
            SharedGRContext.Shutdown();
            RenderThread.Dispatcher.Shutdown();

            BeutlApplication.Current.LoggerFactory.Dispose();

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
