using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class CheckForUpdatesTask : StartupTask
{
    private static readonly ILogger<CheckForUpdatesTask> s_logger = Log.CreateLogger<CheckForUpdatesTask>();
    private readonly BeutlApiApplication _beutlApiApplication;
    private readonly INotificationService _notifications;

    public CheckForUpdatesTask(BeutlApiApplication beutlApiApplication, IServiceProvider services)
    {
        _beutlApiApplication = beutlApiApplication;
        _notifications = services.GetRequiredService<INotificationService>();
        var controller = services.GetRequiredService<AppUpdateController>();
        Task = Task.Run(async () =>
        {
            using Activity? activity = Telemetry.StartActivity("CheckForUpdatesTask");
            activity?.AddEvent(new("Checking for updates"));
            var (v1, v3) = await CheckForUpdates(activity);
            activity?.AddEvent(new("Checked for updates"));
            await HandleUpdateCheckAsync(v1, v3, controller, _notifications, activity);
        });
    }

    public override Task Task { get; }

    internal static async Task HandleUpdateCheckAsync(CheckForUpdatesResponse? v1, AppUpdateResponse? v3,
        AppUpdateController controller, INotificationService notifications, Activity? activity = null)
    {
        bool isLatest = v1?.IsLatest ?? v3?.IsLatest ?? true;
        bool mustLatest = v1?.MustLatest ?? v3?.MustLatest ?? false;
        activity?.SetTag("IsLatest", isLatest);
        activity?.SetTag("MustLatest", mustLatest);
        UpdateCheckResult result = Classify(isLatest, mustLatest);
        if (result == UpdateCheckResult.UpToDate) return;

        string latestVersion = v1?.LatestVersion ?? v3!.LatestVersion;
        if (result == UpdateCheckResult.UpgradeRequired)
            s_logger.LogWarning("Current version must be updated to {LatestVersion}.", latestVersion);
        else
            s_logger.LogInformation("A new version is available: {LatestVersion}", latestVersion);

        AppUpdateResponse? inAppUpdate = v3?.DownloadUrl != null && !FlatpakUpdater.RequiresManualUpdate ? v3 : null;
        if (inAppUpdate != null && await controller.TryStartAutomaticallyAsync(inAppUpdate))
            return;

        string? releaseUrl = v1?.Url ?? v3?.Url;
        if (releaseUrl == null)
        {
            s_logger.LogWarning("Update {LatestVersion} had no URL; falling back to releases page.", latestVersion);
            activity?.SetTag("UpdateUrlMissing", true);
            releaseUrl = AppUpdateController.ReleasesPageUrl;
        }

        if (result == UpdateCheckResult.UpgradeRequired && inAppUpdate == null)
            await ShowUpgradeRequiredDialog(releaseUrl, notifications);
        else
            ShowUpdateAvailable(latestVersion, releaseUrl, inAppUpdate, controller, notifications);
    }

    // The server sets mustLatest only together with isLatest = false, when the running version
    // is below the release's minimum version, so mustLatest has to be checked first.
    internal static UpdateCheckResult Classify(bool isLatest, bool mustLatest)
    {
        if (mustLatest)
            return UpdateCheckResult.UpgradeRequired;

        return isLatest ? UpdateCheckResult.UpToDate : UpdateCheckResult.UpdateAvailable;
    }

    // Closes Beutl only when the release page opened, so the user is never locked out of the update.
    // "No" keeps it running so projects can still be saved; the dialog shows again on the next start.
    internal static void HandleUpgradeRequiredAnswer(
        FAContentDialogResult answer,
        Func<bool> openReleasePage,
        Action closeApp)
    {
        if (answer == FAContentDialogResult.Primary && openReleasePage())
        {
            closeApp();
        }
    }

    private static void ShowUpdateAvailable(string latestVersion, string releaseUrl, AppUpdateResponse? inAppUpdate,
        AppUpdateController controller, INotificationService notifications)
    {
        NotificationAction releaseNotes = new(MessageStrings.ReleaseNotes, () => AppUpdateController.OpenUrl(notifications, releaseUrl));
        bool required = inAppUpdate?.MustLatest == true;
        notifications.Show(
            required ? MessageStrings.UpgradeRequired : MessageStrings.NewVersionAvailable,
            AppUpdateController.GetVersionLabel(latestVersion),
            required ? NotificationType.Warning : NotificationType.Information,
            actions: inAppUpdate is { } update
                ? [new(MessageStrings.UpdateAction_Download, () =>
                {
                    // The update takes the claim when it can, so that no other Beutl starts one unasked. One that
                    // unpacks stops before downloading while another Beutl holds it, says why, and can be retried.
                    _ = UpdateDialogViewModel.ClaimUpdateFolders();
                    controller.StartInBackground(update);
                }), releaseNotes]
                : [releaseNotes]);
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
            s_logger.LogError(ex, "An error occurred while checking for updates");
            if (ex is OperationCanceledException)
            {
                // No external token is passed, so an OCE is a network timeout. Show a dedicated
                // message, since Handle ignores OCE.
                _notifications.ShowError(Strings.Error, MessageStrings.UpdateCheckTimedOut);
            }
            else
            {
                await ex.Handle(_notifications);
            }
            return default;
        }
    }

    // The only way to obtain this required update is through its release page.
    private static async Task ShowUpgradeRequiredDialog(string releaseUrl, INotificationService notifications)
    {
        await App.WaitWindowOpened();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new FAContentDialog
            {
                Title = MessageStrings.UpgradeRequired,
                Content = MessageStrings.VersionDiscontinued,
                PrimaryButtonText = Strings.Open,
                CloseButtonText = Strings.Close,
            };

            FAContentDialogResult answer = await dialog.ShowAsync();
            HandleUpgradeRequiredAnswer(
                answer,
                () => AppUpdateController.OpenUrl(notifications, releaseUrl),
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
