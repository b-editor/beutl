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
                ResolvePackageDependenciesTask resolveTask =
                    startup.GetTask<ResolvePackageDependenciesTask>();
                await resolveTask.Task;

                HashSet<string> failedPackageIds = new(
                    resolveTask.Failures.Select(f => f.Package.Id),
                    StringComparer.OrdinalIgnoreCase);

                CrashRecoveryPromptTask promptTask = startup.GetTask<CrashRecoveryPromptTask>();
                await promptTask.Task;

                if (!promptTask.IsRestrictedMode)
                {
                    IReadOnlyList<LocalPackage> packages = await _manager.GetPackages();

                    activity?.AddEvent(new ActivityEvent("Started loading installed packages."));

                    foreach (LocalPackage item in packages.Where(item => failedPackageIds.Contains(item.Name)))
                    {
                        _logger.LogWarning(
                            "Skipping package {PackageName} due to dependency re-resolution failure.",
                            item.Name);
                        Failures.Add((item, new InvalidOperationException(
                            $"Dependency re-resolution failed for package '{item.Name}'.")));
                    }

                    await LoadPackagesAsync(
                        packages.Where(item => !failedPackageIds.Contains(item.Name)),
                        _manager.Prepare,
                        startup.GetTask<LoadPrimitiveExtensionTask>().Task,
                        prepared => _manager.Load(prepared),
                        _logger,
                        activity,
                        Failures);

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

    // Assemblies load in parallel; Load() then runs after loadAfter, one package at a time in name order.
    internal static async Task LoadPackagesAsync(
        IEnumerable<LocalPackage> packages,
        Func<LocalPackage, PreparedPackage?> prepare,
        Task loadAfter,
        Action<PreparedPackage> load,
        ILogger logger,
        Activity? activity,
        ConcurrentBag<(LocalPackage, Exception)> failures)
    {
        var prepared = new ConcurrentBag<PreparedPackage>();
        Parallel.ForEach(packages, package => TryLoadPackage(package, () =>
        {
            if (prepare(package) is { } result)
                prepared.Add(result);
        }, logger, activity, failures));

        await loadAfter.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        foreach (PreparedPackage package in prepared.OrderBy(item => item.Package.Name, StringComparer.OrdinalIgnoreCase))
        {
            TryLoadPackage(package.Package, () => load(package), logger, activity, failures);
        }
    }

    // A package that fails to load is recorded for the failure report and does not stop the others.
    private static void TryLoadPackage(
        LocalPackage package,
        Action load,
        ILogger logger,
        Activity? activity,
        ConcurrentBag<(LocalPackage, Exception)> failures)
    {
        try
        {
            load();
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogError(e, "Failed to load package: {PackageName}", package.Name);
            failures.Add((package, e));
        }
    }
}
