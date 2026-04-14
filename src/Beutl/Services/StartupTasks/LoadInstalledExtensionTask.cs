using System.Collections.Concurrent;

using Avalonia.Threading;

using Beutl.Api.Services;
using Beutl.Logging;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class LoadInstalledExtensionTask : StartupTask
{
    private readonly ILogger<LoadInstalledExtensionTask> _logger = Log.CreateLogger<LoadInstalledExtensionTask>();
    private readonly PackageManager _manager;

    public LoadInstalledExtensionTask(PackageManager manager, Startup startup)
    {
        _manager = manager;

        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("LoadInstalledExtensionTask"))
            {
                // 依存関係の再復元完了を待機
                ResolvePackageDependenciesTask resolveTask =
                    startup.GetTask<ResolvePackageDependenciesTask>();
                await resolveTask.Task;

                // 依存関係の再復元に失敗したパッケージIDを収集
                HashSet<string> failedPackageIds = new(
                    resolveTask.Failures.Select(f => f.Package.Id),
                    StringComparer.OrdinalIgnoreCase);

                // .beutl/packages/ 内のパッケージを読み込む
                if (!await AsksRunInRestrictedMode())
                {
                    IReadOnlyList<LocalPackage> packages = await _manager.GetPackages();

                    activity?.AddEvent(new ActivityEvent("Started loading installed packages."));

                    Parallel.ForEach(packages, item =>
                    {
                        if (failedPackageIds.Contains(item.Name))
                        {
                            _logger.LogWarning(
                                "Skipping package {PackageName} due to dependency re-resolution failure.",
                                item.Name);
                            Failures.Add((item, new InvalidOperationException(
                                $"Dependency re-resolution failed for package '{item.Name}'.")));
                            return;
                        }

                        try
                        {
                            _manager.Load(item);
                        }
                        catch (Exception e)
                        {
                            activity?.SetStatus(ActivityStatusCode.Error);
                            _logger.LogError(e, "Failed to load package: {PackageName}", item.Name);
                            Failures.Add((item, e));
                        }
                    });

                    activity?.AddEvent(new ActivityEvent("Finished loading installed packages."));
                }
                else
                {
                    _logger.LogWarning("Running in restricted mode, skipping package loading.");
                }
            }
        });
    }

    public override Task Task { get; }

    public ConcurrentBag<(LocalPackage, Exception)> Failures { get; } = [];

    // 最後に実行したとき、例外が発生して終了した場合、
    // 制限モード (拡張機能を読み込まない) で起動するかを尋ねる。
    private static async ValueTask<bool> AsksRunInRestrictedMode()
    {
        if (UnhandledExceptionHandler.LastExecutionExceptionWasThrown())
        {
            await App.WaitWindowOpened();
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new FAContentDialog()
                {
                    Title = MessageStrings.PreviousSessionErrorTitle,
                    Content = MessageStrings.RestrictedModePrompt,
                    PrimaryButtonText = Strings.Yes,
                    CloseButtonText = Strings.No
                };

                return await dialog.ShowAsync() == FAContentDialogResult.Primary;
            });
        }
        else
        {
            return false;
        }
    }
}
