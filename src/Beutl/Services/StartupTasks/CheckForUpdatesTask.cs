using System.ComponentModel;
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
                            // /app is read-only in Flatpak so the in-app updater cannot overwrite
                            // AppContext.BaseDirectory. Open the release page instead and let the
                            // user update through Flathub / `flatpak update`. We deliberately do NOT
                            // fall back to DownloadUrl: it points at the raw .flatpak asset, which
                            // contradicts the Flathub-update guidance and leaves the user with a
                            // bundle they cannot install from inside the sandbox.
                            string releaseUrl;
                            if (v3.Url is { } url)
                            {
                                releaseUrl = url;
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "AppUpdateResponse for {LatestVersion} had no Url; falling back to releases page.",
                                    v3.LatestVersion);
                                activity?.SetTag("UpdateUrlMissing", true);
                                releaseUrl = "https://github.com/b-editor/beutl/releases";
                            }
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
        // /.flatpak-info is bind-mounted by the Flatpak runtime; FLATPAK_ID is checked as a
        // defensive fallback in case the file probe misbehaves on unusual mount namespaces.
        return File.Exists("/.flatpak-info")
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex) when (ex is Win32Exception
                                      or InvalidOperationException
                                      or FileNotFoundException
                                      or PlatformNotSupportedException
                                      or IOException)
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
