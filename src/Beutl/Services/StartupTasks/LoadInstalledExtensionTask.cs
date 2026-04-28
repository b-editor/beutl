using System.Collections.Concurrent;

using Beutl.Api.Services;
using Beutl.Logging;

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

                // クラッシュ復旧プロンプトの結果を待機し、制限モードか判定
                CrashRecoveryPromptTask promptTask = startup.GetTask<CrashRecoveryPromptTask>();
                await promptTask.Task;
                IsRestrictedMode = promptTask.IsRestrictedMode;

                // .beutl/packages/ 内のパッケージを読み込む
                if (!IsRestrictedMode)
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

    public bool IsRestrictedMode { get; private set; }
}
