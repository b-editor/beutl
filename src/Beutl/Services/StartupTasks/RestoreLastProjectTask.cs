using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class RestoreLastProjectTask : StartupTask
{
    private readonly ILogger<RestoreLastProjectTask> _logger = Log.CreateLogger<RestoreLastProjectTask>();

    public RestoreLastProjectTask(Startup startup)
    {
        Task = System.Threading.Tasks.Task.Run(async () =>
        {
            using Activity? activity = Telemetry.StartActivity("RestoreLastProjectTask");

            LoadInstalledExtensionTask loadTask = startup.GetTask<LoadInstalledExtensionTask>();
            await loadTask.Task;

            if (!UnhandledExceptionHandler.LastExecutionExceptionWasThrown())
            {
                return;
            }

            if (loadTask.IsRestrictedMode)
            {
                _logger.LogInformation("Restricted mode: skipping project restore prompt.");
                return;
            }

            string? file = GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                _logger.LogInformation("No last opened project to restore.");
                return;
            }

            if (ProjectService.Current.CurrentProject.Value is not null)
            {
                return;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            {
                return;
            }

            await App.WaitWindowOpened();

            bool yes = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = MessageStrings.RestoreLastProjectTitle,
                    Content = string.Format(MessageStrings.RestoreLastProjectPrompt, Path.GetFileName(file)),
                    PrimaryButtonText = Strings.Yes,
                    CloseButtonText = Strings.No,
                };

                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            });

            if (!yes)
            {
                GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => ProjectService.Current.OpenProject(file));
        });
    }

    public override Task Task { get; }
}
