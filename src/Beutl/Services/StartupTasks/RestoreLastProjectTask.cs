using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class RestoreLastProjectTask : StartupTask
{
    private readonly ILogger<RestoreLastProjectTask> _logger = Log.CreateLogger<RestoreLastProjectTask>();

    public RestoreLastProjectTask(Startup startup)
    {
        Task = Task.Run(async () =>
        {
            using Activity? activity = Telemetry.StartActivity("RestoreLastProjectTask");

            LoadInstalledExtensionTask loadTask = startup.GetTask<LoadInstalledExtensionTask>();
            await loadTask.Task;

            CrashRecoveryPromptTask promptTask = startup.GetTask<CrashRecoveryPromptTask>();
            await promptTask.Task;

            if (!promptTask.RestoreLastProject)
            {
                return;
            }

            string? file = promptTask.LastProjectFile;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                _logger.LogInformation("Skipping project restore: file is unavailable.");
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

            await Dispatcher.UIThread.InvokeAsync(() => ProjectService.Current.OpenProject(file));
        });
    }

    public override Task Task { get; }
}
