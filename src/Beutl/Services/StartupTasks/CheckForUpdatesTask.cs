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
                    if (!v1.IsLatest)
                    {
                        _logger.LogInformation("A new version is available: {VersionUrl}", v1.Url);
                        NotificationService.ShowInformation(
                            MessageStrings.NewVersionAvailable,
                            v1.Url,
                            onActionButtonClick: () => OpenUrl(v1.Url),
                            actionButtonText: Strings.Open);
                    }
                    else if (v1.MustLatest)
                    {
                        _logger.LogWarning("Current version must be updated to the latest version.");
                        await ShowDialogAndClose(v1);
                    }
                }
                else if (v3 != null)
                {
                    if (!v3.IsLatest)
                    {
                        _logger.LogInformation("A new version is available: {DownloadUrl}", v3.DownloadUrl);
                        bool isFlatpak = IsFlatpak();
                        _logger.LogDebug("Flatpak sandbox detected: {IsFlatpak}", isFlatpak);
                        if (isFlatpak)
                        {
                            // /app is read-only in Flatpak so the standalone-zip in-app updater (the else
                            // branch below) cannot overwrite AppContext.BaseDirectory. Send the user to
                            // the release page so the update happens through Flathub / `flatpak update`.
                            // We deliberately do NOT fall back to DownloadUrl: the standalone-zip updater
                            // cannot install a Flatpak bundle from inside the sandbox, so following it
                            // would either fail or pull the user off the Flathub update path.
                            string releaseUrl = GetReleaseUrl(v3, activity);
                            NotificationService.ShowInformation(
                                MessageStrings.NewVersionAvailable,
                                releaseUrl,
                                onActionButtonClick: () => OpenUrl(releaseUrl),
                                actionButtonText: Strings.Open);
                        }
                        else
                        {
                            NotificationService.ShowInformation(
                                MessageStrings.NewVersionAvailable,
                                message: MessageStrings.ConfirmInstall,
                                onActionButtonClick: () =>
                                {
                                    var viewModel = new UpdateDialogViewModel(v3);
                                    var dialog = new UpdateDialog { DataContext = viewModel };
                                    dialog.ShowAsync();
                                    viewModel.Start();
                                },
                                // TODO: Stringsに移動
                                actionButtonText: ExtensionsStrings.Install);
                        }
                    }
                    else if (v3.MustLatest)
                    {
                        _logger.LogWarning("Current version must be updated to the latest version.");
                        string releaseUrl = GetReleaseUrl(v3, activity);
                        await ShowDialogAndClose(new CheckForUpdatesResponse
                        {
                            Url = releaseUrl,
                            IsLatest = v3.IsLatest,
                            MustLatest = v3.MustLatest,
                            LatestVersion = v3.LatestVersion
                        });
                    }
                }
            }
        });
    }

    public override Task Task { get; }

    private static bool IsFlatpak()
    {
        // /.flatpak-info is the canonical sandbox marker (bind-mounted read-only by the runtime).
        // FLATPAK_ID is checked as a backup signal so detection still works if the marker file is
        // ever moved or hidden by host customizations.
        return File.Exists("/.flatpak-info")
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));
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

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to open URL: {Url}", url);
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async ValueTask<(CheckForUpdatesResponse? V1, AppUpdateResponse? V3)> CheckForUpdates(Activity? activity)
    {
        try
        {
            return await _beutlApiApplication.CheckForUpdatesAsync(BeutlApplication.Version);
        }
        // TaskCanceledException is raised by HttpClient on timeout; we want to log/notify that
        // like any other failure. Pure OperationCanceledException (e.g. app shutdown) bubbles up.
        catch (Exception ex) when (ex is TaskCanceledException || ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "An error occurred while checking for updates");
            if (ex is TaskCanceledException)
            {
                // DefaultExceptionHandler.Handle silently swallows OperationCanceledException
                // and all its subclasses, so notify the user directly for HTTP timeouts.
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
            else
            {
                await ex.Handle();
            }
            return default;
        }
    }

    private async Task ShowDialogAndClose(CheckForUpdatesResponse response)
    {
        await App.WaitWindowOpened();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = MessageStrings.UpgradeRequired,
                Content = MessageStrings.VersionDiscontinued,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
            };

            await dialog.ShowAsync();

            OpenUrl(response.Url);

            (AppHelper.GetTopLevel() as Window)?.Close();
        });
    }
}
