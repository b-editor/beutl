using Beutl.Editor.Components.Helpers;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

// Runs a store page command while isBusy is set. A cancellation from the page's lifetime is dropped; any
// other failure is reported and logged.
internal static class StorePageCommand
{
    public static async Task RunAsync(
        ReactivePropertySlim<bool> isBusy,
        string? activityName,
        CancellationToken lifetime,
        ILogger logger,
        Func<Activity?, Task> body)
    {
        using Activity? activity = activityName is null ? null : Telemetry.StartActivity(activityName);

        try
        {
            isBusy.Value = true;
            await body(activity);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            await e.Handle();
            logger.LogError(e, "An unexpected error has occurred.");
        }
        finally
        {
            isBusy.Value = false;
        }
    }
}
