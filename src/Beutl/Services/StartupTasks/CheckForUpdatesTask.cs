using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class CheckForUpdatesTask : StartupTask
{
    private readonly ILogger<CheckForUpdatesTask> _logger = Log.CreateLogger<CheckForUpdatesTask>();
    private readonly BeutlApiApplication _beutlApiApplication;

    public CheckForUpdatesTask(BeutlApiApplication beutlApiApplication)
    {
        _beutlApiApplication = beutlApiApplication;
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("CheckForUpdatesTask"))
            {
                activity?.AddEvent(new("Checking for updates"));
                var (v1, v3) = await CheckForUpdates(activity);
                activity?.AddEvent(new("Checked for updates"));
                activity?.SetTag("IsLatest", v1?.IsLatest ?? v3?.IsLatest ?? true);
                activity?.SetTag("MustLatest", v1?.MustLatest ?? v3?.MustLatest ?? false);

                if (v1 != null)
                {
                    UpdateCheckResult result = Classify(v1.IsLatest, v1.MustLatest);
                    if (result == UpdateCheckResult.UpgradeRequired)
                    {
                        _logger.LogWarning("Current version must be updated to the latest version.");
                        await ShowUpgradeRequiredDialog(v1.Url);
                    }
                    else if (result == UpdateCheckResult.UpdateAvailable)
                    {
                        _logger.LogInformation("A new version is available: {VersionUrl}", v1.Url);
                        NotificationService.ShowInformation(
                            MessageStrings.NewVersionAvailable,
                            v1.Url,
                            actions: [new(Strings.Open, () => OpenUrl(v1.Url))]);
                    }
                }
                else if (v3 != null)
                {
                    UpdateCheckResult result = Classify(v3.IsLatest, v3.MustLatest);
                    if (result == UpdateCheckResult.UpgradeRequired)
                    {
                        _logger.LogWarning("Current version must be updated to the latest version.");
                        await ShowUpgradeRequiredDialog(GetReleaseUrl(v3, activity));
                    }
                    else if (result == UpdateCheckResult.UpdateAvailable)
                    {
                        _logger.LogInformation("A new version is available: {DownloadUrl}", v3.DownloadUrl);
                        if (FlatpakUpdater.RequiresManualUpdate)
                        {
                            string releaseUrl = GetReleaseUrl(v3, activity);
                            NotificationService.ShowInformation(MessageStrings.NewVersionAvailable, releaseUrl,
                                actions: [new(Strings.Open, () => OpenUrl(releaseUrl))]);
                            return;
                        }
                        NotificationService.ShowInformation(
                            MessageStrings.NewVersionAvailable,
                            message: MessageStrings.ConfirmInstall,
                            actions:
                            [
                                new(ExtensionsStrings.Install, () =>
                                {
                                    var viewModel = new UpdateDialogViewModel(v3);
                                    var dialog = new UpdateDialog { DataContext = viewModel };
                                    dialog.ShowAsync();
                                    viewModel.Start();
                                })
                            ]);
                    }
                }
            }
        });
    }

    public override Task Task { get; }

    // The server sets mustLatest only together with isLatest = false, when the running version
    // is below the release's minimum version, so mustLatest has to be checked first.
    internal static UpdateCheckResult Classify(bool isLatest, bool mustLatest)
    {
        if (mustLatest)
            return UpdateCheckResult.UpgradeRequired;

        return isLatest ? UpdateCheckResult.UpToDate : UpdateCheckResult.UpdateAvailable;
    }

    // "No" keeps Beutl running and does not open the browser. Quitting would leave users who cannot
    // update right away with no way to open or save their projects; the dialog shows again on the next start.
    internal static void HandleUpgradeRequiredAnswer(
        FAContentDialogResult answer,
        Func<bool> openReleasePage,
        Action closeApp)
    {
        // Only close the app if we actually managed to send the user to the update page.
        // Otherwise the user would be locked out without any way to obtain the new version.
        if (answer == FAContentDialogResult.Primary && openReleasePage())
        {
            closeApp();
        }
    }

    private string GetReleaseUrl(AppUpdateResponse v3, Activity? activity)
    {
        if (v3.Url is { } url)
        {
            return url;
        }

        _logger.LogWarning(
            "AppUpdateResponse for {LatestVersion} had no Url; falling back to releases page.",
            v3.LatestVersion);
        activity?.SetTag("UpdateUrlMissing", true);
        return "https://github.com/b-editor/beutl/releases";
    }

    private bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to open URL: {Url}", url);
            NotificationService.ShowError(Strings.Error, ex.Message);
            return false;
        }
    }

    private async ValueTask<(CheckForUpdatesResponse? V1, AppUpdateResponse? V3)> CheckForUpdates(Activity? activity)
    {
        try
        {
            return await _beutlApiApplication.CheckForUpdatesAsync(BeutlApplication.Version, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (_beutlApiApplication.IsDisposed
                && ex is OperationCanceledException or ObjectDisposedException)
            {
                // Shutdown cancellation is a normal exit, not a failed update check:
                // skip the error telemetry and the timeout notification.
                return default;
            }

            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "An error occurred while checking for updates");
            if (ex is OperationCanceledException)
            {
                // HttpClient timeouts surface as TaskCanceledException or, on modern .NET,
                // sometimes as plain OperationCanceledException. No external CancellationToken
                // is passed here, so any OCE is effectively a network timeout — show a
                // localized message instead of the generic "A task was canceled." text, and
                // skip DefaultExceptionHandler.Handle, which silently swallows OCE subclasses.
                NotificationService.ShowError(Strings.Error, MessageStrings.UpdateCheckTimedOut);
            }
            else
            {
                await ex.Handle();
            }
            return default;
        }
    }

    private async Task ShowUpgradeRequiredDialog(string releaseUrl)
    {
        await App.WaitWindowOpened();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new FAContentDialog
            {
                Title = MessageStrings.UpgradeRequired,
                Content = MessageStrings.VersionDiscontinued,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
            };

            FAContentDialogResult answer = await dialog.ShowAsync();
            HandleUpgradeRequiredAnswer(
                answer,
                () => OpenUrl(releaseUrl),
                () => (AppHelper.GetTopLevel() as Window)?.Close());
        });
    }

    internal enum UpdateCheckResult
    {
        UpToDate,
        UpdateAvailable,
        UpgradeRequired,
    }
}
