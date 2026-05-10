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
                            onActionButtonClick: () =>
                            {
                                Process.Start(new ProcessStartInfo(v1.Url) { UseShellExecute = true, Verb = "open" });
                            },
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
                        if (IsFlatpak())
                        {
                            // /app is read-only in Flatpak; the in-app updater would overwrite
                            // AppContext.BaseDirectory and always fail. Defer to `flatpak update`.
                            string releaseUrl = v3.Url ?? v3.DownloadUrl ?? "https://github.com/b-editor/beutl/releases";
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
                        await ShowDialogAndClose(new CheckForUpdatesResponse
                        {
                            Url = v3.Url!,
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
        // /.flatpak-info is bind-mounted by the Flatpak runtime even when FLATPAK_ID is stripped
        // (e.g. `flatpak run --command=bash` followed by re-exec).
        return File.Exists("/.flatpak-info")
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open URL: {Url}", url);
            NotificationService.ShowError(MessageStrings.OperationFailed, url);
        }
    }

    private async ValueTask<(CheckForUpdatesResponse? V1, AppUpdateResponse? V3)> CheckForUpdates(Activity? activity)
    {
        try
        {
            return await _beutlApiApplication.CheckForUpdatesAsync(BeutlApplication.Version);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "An error occurred while checking for updates");
            await ex.Handle();
            return default;
        }
    }

    private static async Task ShowDialogAndClose(CheckForUpdatesResponse response)
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

            Process.Start(new ProcessStartInfo(response.Url) { UseShellExecute = true, Verb = "open" });

            (AppHelper.GetTopLevel() as Window)?.Close();
        });
    }
}
