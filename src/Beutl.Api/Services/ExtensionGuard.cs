using System.Runtime.CompilerServices;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

// Calls into an extension from the host's factory and command entry points. A failure is logged and reported once per
// extension; an extension from a package is then skipped until restart, while Beutl's own stay enabled.
internal static class ExtensionGuard
{
    private static readonly ConditionalWeakTable<Extension, string> s_packages = [];
    private static readonly ConditionalWeakTable<Extension, object> s_failed = [];
    private static ILogger? s_logger;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(ExtensionGuard));

    public static void SetPackage(Extension extension, string package) => s_packages.AddOrUpdate(extension, package);

    public static bool IsTurnedOff(Extension extension)
        => s_packages.TryGetValue(extension, out _) && s_failed.TryGetValue(extension, out _);

    // The result, or null when the extension threw or is turned off.
    public static T? Call<T>(Extension extension, Func<T?> call)
        where T : class
    {
        if (IsTurnedOff(extension))
            return null;

        try
        {
            return call();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Report(extension, exception);
            return null;
        }
    }

    // The result, or false when the extension threw or is turned off.
    public static bool Call(Extension extension, Func<bool> call)
    {
        if (IsTurnedOff(extension))
            return false;

        try
        {
            return call();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Report(extension, exception);
            return false;
        }
    }

    // Cancellation is not a failure, so it reaches the caller.
    public static async Task CallAsync(Extension extension, Func<Task> call)
    {
        if (IsTurnedOff(extension))
            return;

        try
        {
            await call();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            Report(extension, exception);
        }
    }

    private static void Report(Extension extension, Exception exception)
    {
        if (!s_failed.TryAdd(extension, exception))
            return;

        string? package = s_packages.TryGetValue(extension, out string? name) ? name : null;
        Logger.LogError(exception, "{Extension} from {Package} failed.", extension.GetType().FullName, package ?? "Beutl");
        NotificationService.ShowError(
            string.Format(ExtensionsStrings.ExtensionFailed, DisplayName(extension)),
            package is null ? MessageStrings.OperationFailed : string.Format(ExtensionsStrings.ExtensionTurnedOff, package));
    }

    // DisplayName is extension code too.
    private static string DisplayName(Extension extension)
    {
        try
        {
            return extension.DisplayName;
        }
        catch (Exception)
        {
            return extension.GetType().Name;
        }
    }
}
