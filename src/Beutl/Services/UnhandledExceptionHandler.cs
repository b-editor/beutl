using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public static class UnhandledExceptionHandler
{
    private const string CrashRecoveryMarkerFileName = "last-unhandled-exeption";
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
        return File.Exists(GetCrashRecoveryMarkerPath());
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception ex)
            {
                s_logger?.LogCritical(ex, "An unhandled exception occurred. (IsTerminating: {IsTerminating})", e.IsTerminating);

                //var stack = new StackTrace();
                //var fr = stack.GetFrames();
                //Todo: スタックトレースからどこの拡張機能が例外を投げたかを追跡したい
            }

            MarkCrashState();

            PrivateExit();

            Process.Start(CreateExceptionHandlerStartInfo());
        }
        catch
        {
        }
    }

    private static void MarkCrashState()
    {
        try
        {
            TelemetryUncleanSessionMarker.Mark();
        }
        catch
        {
        }

        try
        {
            CreateEmptyMarker(GetCrashRecoveryMarkerPath());
        }
        catch
        {
        }
    }

    internal static ProcessStartInfo CreateExceptionHandlerStartInfo()
    {
        string exePath = Path.Combine(
            AppContext.BaseDirectory,
            "Beutl.ExceptionHandler");

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true
        };
        DotNetProcess.Configure(startInfo, exePath);
        return startInfo;
    }

    private static string GetCrashRecoveryMarkerPath()
    {
        return Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), CrashRecoveryMarkerFileName);
    }

    private static void CreateEmptyMarker(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(directory, $".{CrashRecoveryMarkerFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough))
            {
                // Crash recovery only needs an atomic presence marker.
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The telemetry marker and local log remain sufficient evidence.
            }
        }
    }

    private static void PrivateExit()
    {
        if (!s_exited)
        {
            GlobalConfiguration.Instance.Save(GlobalConfiguration.DefaultFilePath);

            GraphicsContextFactory.Shutdown();
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

            string path = GetCrashRecoveryMarkerPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            TelemetryUncleanSessionMarker.Clear();
        }
    }
}
