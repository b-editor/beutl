using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class CrashRecoveryPromptTask : StartupTask
{
    private readonly ILogger<CrashRecoveryPromptTask> _logger = Log.CreateLogger<CrashRecoveryPromptTask>();

    public CrashRecoveryPromptTask()
    {
        Task = Task.Run(async () =>
        {
            using Activity? activity = Telemetry.StartActivity("CrashRecoveryPromptTask");

            if (!UnhandledExceptionHandler.LastExecutionExceptionWasThrown())
            {
                return;
            }

            string? lastProjectFile = GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile;
            bool canRestoreProject = !string.IsNullOrEmpty(lastProjectFile) && File.Exists(lastProjectFile);
            LastProjectFile = canRestoreProject ? lastProjectFile : null;

            await App.WaitWindowOpened();

            (bool restrictedMode, bool restoreProject) = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var restrictedModeCheckBox = new CheckBox
                {
                    Content = MessageStrings.CrashRecoveryRestrictedModeOption,
                    IsChecked = false,
                };

                CheckBox? restoreCheckBox = null;
                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 8,
                };
                stack.Children.Add(new TextBlock
                {
                    Text = MessageStrings.CrashRecoveryDescription,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                });

                if (canRestoreProject)
                {
                    restoreCheckBox = new CheckBox
                    {
                        Content = string.Format(
                            MessageStrings.CrashRecoveryRestoreOption,
                            Path.GetFileName(lastProjectFile)),
                        IsChecked = true,
                    };
                    stack.Children.Add(restoreCheckBox);
                }

                stack.Children.Add(restrictedModeCheckBox);

                var dialog = new ContentDialog
                {
                    Title = MessageStrings.PreviousSessionErrorTitle,
                    Content = stack,
                    PrimaryButtonText = Strings.OK,
                };

                await dialog.ShowAsync();

                return (
                    restrictedModeCheckBox.IsChecked == true,
                    restoreCheckBox?.IsChecked == true);
            });

            IsRestrictedMode = restrictedMode;
            RestoreLastProject = restoreProject;

            if (restrictedMode)
            {
                _logger.LogInformation("User opted to start in restricted mode.");
            }

            if (canRestoreProject && !restoreProject)
            {
                GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
            }
        });
    }

    public override Task Task { get; }

    public bool IsRestrictedMode { get; private set; }

    public bool RestoreLastProject { get; private set; }

    public string? LastProjectFile { get; private set; }
}
