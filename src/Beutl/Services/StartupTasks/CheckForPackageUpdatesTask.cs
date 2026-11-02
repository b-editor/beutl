using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class CheckForPackageUpdatesTask : StartupTask
{
    private readonly ILogger<CheckForPackageUpdatesTask> _logger = Log.CreateLogger<CheckForPackageUpdatesTask>();

    public CheckForPackageUpdatesTask(Startup startup, PackageManager packageManager, BeutlApiApplication beutlApiApplication)
        : this(
            startup.WaitLoadingExtensions,
            () => startup.GetTask<LoadInstalledExtensionTask>().Failures,
            packageManager.CheckUpdate,
            () => beutlApiApplication.IsDisposed)
    {
    }

    internal CheckForPackageUpdatesTask(
        Func<Task> waitLoadingExtensions,
        Func<IEnumerable<(LocalPackage, Exception)>> getLoadFailures,
        Func<CancellationToken, Task<IReadOnlyList<PackageUpdate>>> checkUpdate,
        Func<bool> isShuttingDown)
    {
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("CheckForPackageUpdatesTask"))
            {
                IReadOnlyList<PackageUpdate> updates = [];
                try
                {
                    await waitLoadingExtensions();

                    activity?.AddEvent(new("Checking for package updates"));

                    updates = await checkUpdate(CancellationToken.None);
                    activity?.SetTag("UpdateCount", updates.Count);

                    if (updates.Count > 0)
                    {
                        _logger.LogInformation("{Count} package update(s) available.", updates.Count);
                    }
                    else
                    {
                        _logger.LogInformation("All packages are up to date.");
                    }
                }
                catch (Exception ex) when (isShuttingDown()
                    && ex is OperationCanceledException or ObjectDisposedException)
                {
                    // Shutdown cancellation is a normal exit, not a failed update check:
                    // skip the error telemetry and the error log.
                    return;
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    _logger.LogError(ex, "An error occurred while checking for package updates.");
                }

                ShowResult(updates, [.. getLoadFailures().Select(failure => failure.Item2).OfType<PackageApiMismatchException>()]);
            }
        });
    }

    public override Task Task { get; }

    // Packages that failed the API check and have an update share one short warning, since updating is
    // the way out. The others keep their full report, as they do when the update check itself failed.
    private static void ShowResult(IReadOnlyList<PackageUpdate> updates, IReadOnlyList<PackageApiMismatchException> mismatches)
    {
        HashSet<string> updatable = new(updates.Select(update => update.Package.Name), StringComparer.OrdinalIgnoreCase);
        PackageApiMismatchException[] outdated = [.. mismatches.Where(mismatch => updatable.Contains(mismatch.Package.Name))];

        foreach (PackageApiMismatchException mismatch in mismatches.Except(outdated))
        {
            AfterLoadingExtensionsTask.ShowMismatch(ExtensionsStrings.PackageNotLoaded, mismatch);
        }

        if (outdated.Length > 0)
        {
            NotificationService.ShowWarning(
                MessageStrings.PackageUpdatesRequired,
                string.Format(MessageStrings.PackagesNotLoadedOnThisVersion, outdated.Length),
                actions: [new(Strings.Open, OpenExtensionsPage)]);
        }

        int others = updates.Count - outdated.Length;
        if (others > 0)
        {
            NotificationService.ShowInformation(
                MessageStrings.PackageUpdatesAvailable,
                string.Format(MessageStrings.PackagesCanBeUpdated, others),
                actions: [new(Strings.Open, OpenExtensionsPage)]);
        }
    }

    private static async void OpenExtensionsPage()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (AppHelper.GetTopLevel() is not Window topLevel)
                    return;

                var extension = ExtensionsToolWindowExtension.Instance;
                if (!extension.TryCreateContext(out IToolWindowContext? context))
                    return;

                if (!extension.TryCreateContent(out Window? window))
                {
                    context.Dispose();
                    return;
                }

                window.DataContext = context;
                if (window is Pages.ExtensionsPage page)
                {
                    page.OpenLibraryWithUpdates();
                }

                try
                {
                    await window.ShowDialog(topLevel);
                }
                finally
                {
                    context.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
    }
}
