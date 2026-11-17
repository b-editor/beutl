using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Beutl.Services.StartupTasks;

public sealed class CheckForPackageUpdatesTask : StartupTask
{
    // Longer than the check takes against a store that answers, even with many packages installed.
    private static readonly TimeSpan s_mismatchDeadline = TimeSpan.FromSeconds(30);

    private readonly ILogger<CheckForPackageUpdatesTask> _logger = Log.CreateLogger<CheckForPackageUpdatesTask>();

    public CheckForPackageUpdatesTask(Startup startup, PackageManager packageManager, BeutlApiApplication beutlApiApplication)
        : this(
            startup.WaitLoadingExtensions,
            () => startup.GetTask<LoadInstalledExtensionTask>().Failures,
            token => CheckAndPrepareAsync(startup, packageManager, token, startup.IsAppUpgrade),
            () => beutlApiApplication.IsDisposed,
            s_mismatchDeadline)
    {
        _ = WatchAutomaticUpdatesAsync(startup, packageManager, beutlApiApplication);
    }

    private static async Task<IReadOnlyList<PackageUpdate>> CheckAndPrepareAsync(
        Startup startup, PackageManager manager, CancellationToken token, bool appUpdate = false)
    {
        if (!GlobalConfiguration.Instance.UpdateConfig.ShouldUpdateExtensions(appUpdate)
            || startup.GetTask<CrashRecoveryPromptTask>().IsRestrictedMode)
            return await manager.CheckUpdate(token);

        await startup.GetTask<AuthenticationTask>().Task;
        var result = await manager.StageExtensionUpdatesAsync(NuGetVersion.Parse(BeutlApplication.Version), appUpdate, token);
        return result.Remaining;
    }

    private async Task WatchAutomaticUpdatesAsync(Startup startup, PackageManager manager, BeutlApiApplication app)
    {
        using var lifetime = app.CreateLifetimeLinkedTokenSource(default);
        CancellationToken token = lifetime.Token;
        using var changed = new SemaphoreSlim(0);
        var setting = GlobalConfiguration.Instance.UpdateConfig.ExtensionUpdates;
        using var subscription = setting.GetObservable().Skip(1)
            .Where(mode => mode == ExtensionUpdateMode.Always).Subscribe(_ => changed.Release());
        try
        {
            await Task.WaitAsync(token);
            if (startup.GetTask<CrashRecoveryPromptTask>().IsRestrictedMode) return;
            while (true)
            {
                await changed.WaitAsync(TimeSpan.FromHours(6), token);
                if (setting.Value != ExtensionUpdateMode.Always) continue;
                try
                {
                    await CheckAndPrepareAsync(startup, manager, token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not check for automatic extension updates.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The application is shutting down.
        }
    }

    internal CheckForPackageUpdatesTask(
        Func<Task> waitLoadingExtensions,
        Func<IEnumerable<(LocalPackage, Exception)>> getLoadFailures,
        Func<CancellationToken, Task<IReadOnlyList<PackageUpdate>>> checkUpdate,
        Func<bool> isShuttingDown,
        TimeSpan mismatchDeadline)
    {
        PackageApiMismatchException[] GetMismatches()
            => [.. getLoadFailures().Select(failure => failure.Item2).OfType<PackageApiMismatchException>()];

        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("CheckForPackageUpdatesTask"))
            {
                IReadOnlyList<PackageUpdate> updates = [];
                bool mismatchesShown = false;
                try
                {
                    await waitLoadingExtensions();

                    activity?.AddEvent(new("Checking for package updates"));

                    Task<IReadOnlyList<PackageUpdate>> check = checkUpdate(CancellationToken.None);
                    if (await Task.WhenAny(check, Task.Delay(mismatchDeadline)) != check)
                    {
                        // A store that does not answer must not hold back which packages did not load.
                        ShowResult([], GetMismatches());
                        mismatchesShown = true;
                    }

                    updates = await check;
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

                ShowResult(updates, mismatchesShown ? [] : GetMismatches());
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
                ColorPaletteService.SetService(window, ColorPaletteService.GetService(topLevel));
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
