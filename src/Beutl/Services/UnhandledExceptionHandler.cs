using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.Styling;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public static class UnhandledExceptionHandler
{
    private const string LastUnhandledExeptionFileName = "last-unhandled-exeption";
    private static bool s_exited;
    private static ILogger? s_logger;
    private static string s_sessionId = string.Empty;

    public static void Initialize(string sessionId, GlobalConfiguration configuration)
    {
        s_sessionId = sessionId;
        AppDomain.CurrentDomain.UnhandledException += (sender, args) => OnUnhandledException(sender, args, configuration);
        s_logger = Log.LoggerFactory.CreateLogger(typeof(UnhandledExceptionHandler));
    }

    // 最後に実行されたとき、例外が発生して終了したかどうか。
    public static bool LastExecutionExceptionWasThrown()
    {
        return File.Exists(Path.Combine(Helper.AppRoot, LastUnhandledExeptionFileName));
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e, GlobalConfiguration configuration)
    {
        try
        {
            if (e.ExceptionObject is Exception ex)
            {
                s_logger?.LogCritical(ex, "An unhandled exception occurred. (IsTerminating: {IsTerminating})", e.IsTerminating);
                SaveException(ex);
            }

            PrivateExit(configuration);

            ProcessStartInfo startInfo = DotNetProcess.CreateBesideApplication("Beutl.ExceptionHandler");
            startInfo.ArgumentList.Add(UiFonts.UiCultureArgument);
            startInfo.ArgumentList.Add(CultureInfo.CurrentUICulture.Name);
            startInfo.ArgumentList.Add("--session-id");
            startInfo.ArgumentList.Add(s_sessionId);
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

    private static void PrivateExit(GlobalConfiguration configuration)
    {
        if (s_exited)
            return;

        // Each step stands alone, so a failure cannot skip the render-thread shutdown or the log flush.
        // The flag is set first so that Exit() does not run the sequence a second time.
        s_exited = true;
        RunOrLog(
            () => configuration.Save(GlobalConfiguration.DefaultFilePath),
            "save the configuration");
        RunOrLog(GraphicsContextFactory.Shutdown, "shut the graphics context down");
        RunOrLog(RenderThread.Dispatcher.Shutdown, "shut the render dispatcher down");
        RunOrLog(BeutlApplication.Current.LoggerFactory.Dispose, "dispose the logger factory");
    }

    private static void RunOrLog(Action step, string description)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            try
            {
                s_logger?.LogError(ex, "Failed to {Step} while exiting.", description);
            }
            catch
            {
                // The logger may already be disposed.
            }
        }
    }

    public static void Exit(GlobalConfiguration configuration)
    {
        if (!s_exited)
        {
            PrivateExit(configuration);

            string path = Path.Combine(Helper.AppRoot, LastUnhandledExeptionFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
