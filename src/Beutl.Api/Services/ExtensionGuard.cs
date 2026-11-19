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

    // The notifications a context offers. A context can come from an extension, so the lookup is extension code
    // that may throw; a failure yields null so that the caller falls back to the host's notifications.
    public static INotificationService? FindNotifications(object? context)
        => FindService<INotificationService>(context);

    public static T? FindService<T>(object? context) where T : class
    {
        try
        {
            return (context as IServiceProvider)?.GetService(typeof(T)) as T;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static T RequireService<T>(object context) where T : class
        => FindService<T>(context) ?? throw new InvalidOperationException($"The editor context must provide {typeof(T).Name}.");

    public static bool IsTurnedOff(Extension extension)
        => s_packages.TryGetValue(extension, out _) && s_failed.TryGetValue(extension, out _);

    // The result, or null when the extension threw or is turned off.
    public static T? Call<T>(Extension extension, INotificationService? notifications, Func<T?> call)
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
            Report(extension, notifications, exception);
            return null;
        }
    }

    // declined is true when the call itself returned null, which the guard has not reported.
    public static T? Call<T>(Extension extension, INotificationService? notifications, Func<T?> call, out bool declined)
        where T : class
    {
        bool returned = false;
        T? result = Call(extension, notifications, () =>
        {
            T? value = call();
            returned = true;
            return value;
        });
        declined = result is null && returned;
        return result;
    }

    // The result, or false when the extension threw or is turned off.
    public static bool Call(Extension extension, INotificationService? notifications, Func<bool> call)
    {
        if (IsTurnedOff(extension))
            return false;

        try
        {
            return call();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Report(extension, notifications, exception);
            return false;
        }
    }

    // Cancellation is not a failure, so it reaches the caller.
    public static async Task CallAsync(Extension extension, INotificationService? notifications, Func<Task> call)
    {
        if (IsTurnedOff(extension))
            return;

        try
        {
            await call();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            Report(extension, notifications, exception);
        }
    }

    private static void Report(Extension extension, INotificationService? notifications, Exception exception)
    {
        if (!s_failed.TryAdd(extension, exception))
            return;

        string? package = s_packages.TryGetValue(extension, out string? name) ? name : null;
        Logger.LogError(exception, "{Extension} from {Package} failed.", extension.GetType().FullName, package ?? "Beutl");
        notifications?.ShowError(
            string.Format(ExtensionsStrings.ExtensionFailed, DisplayName(extension)),
            package is null ? MessageStrings.OperationFailed : string.Format(ExtensionsStrings.ExtensionTurnedOff, package));
    }

    // DisplayName is extension code too.
    public static string DisplayName(Extension extension)
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
