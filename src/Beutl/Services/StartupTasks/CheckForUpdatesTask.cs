using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Clients;
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
                            Message.A_new_version_is_available,
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
                        NotificationService.ShowInformation(
                            Message.A_new_version_is_available,
                            message: Message.Do_you_want_to_install,
                            onActionButtonClick: () =>
                            {
                                var viewModel = new UpdateDialogViewModel(v3);
                                var dialog = new UpdateDialog { DataContext = viewModel };
                                dialog.ShowAsync();
                                viewModel.Start();
                            },
                            // TODO: Stringsに移動
                            actionButtonText: ExtensionsPage.Install);
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
                Title = Message.Must_upgrade_for_continued_use,
                Content = Message.This_version_has_been_discontinued_for_compatibility_reasonsversion,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
            };

            await dialog.ShowAsync();

            Process.Start(new ProcessStartInfo(response.Url) { UseShellExecute = true, Verb = "open" });

            (App.GetTopLevel() as Window)?.Close();
        });
    }
}
