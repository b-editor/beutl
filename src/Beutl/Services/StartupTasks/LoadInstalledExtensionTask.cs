using System.Collections.Concurrent;

using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Logging;

using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

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
                    activity?.AddEvent(new ActivityEvent("Started loading installed packages."));
                    await LoadInstalledAsync(startup, failedPackageIds, activity);
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

    private async Task LoadInstalledAsync(Startup startup, HashSet<string> failedPackageIds, Activity? activity)
    {
        var operations = startup.CreatePackageOperations();
        NuGetVersion host = NuGetVersion.Parse(BeutlApplication.Version);
        PackageIdentity[] installed = [.. operations.InstalledPackageRepository.GetLocalPackages()
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)];
        await PrepareExternalUpdateAsync(startup, installed, host);

        // A staged replacement loads in the same position as the original extension. The packages
        // before and after it still prepare in parallel, just as on a launch without updates.
        Task primitives = startup.GetTask<LoadPrimitiveExtensionTask>().Task;
        var batch = new List<LocalPackage>();
        async Task LoadBatchAsync()
        {
            await LoadPackagesAsync(batch, _manager.Prepare, primitives, prepared => _manager.Load(prepared), _logger, activity, Failures);
            batch.Clear();
        }

        foreach (PackageIdentity package in installed)
        {
            if (await operations.TryApplyStagedExtensionUpdateAsync(package, host, LoadBatchAsync)) continue;
            if (PackageManager.ReadInstalledPackage(package) is not { } item) continue;
            if (failedPackageIds.Contains(item.Name))
            {
                _logger.LogWarning("Skipping package {PackageName} due to dependency re-resolution failure.", item.Name);
                Failures.Add((item, new InvalidOperationException($"Dependency re-resolution failed for package '{item.Name}'.")));
            }
            else
            {
                batch.Add(item);
            }
        }
        await LoadBatchAsync();
    }

    private async Task PrepareExternalUpdateAsync(Startup startup, PackageIdentity[] installed, NuGetVersion host)
    {
        GlobalConfiguration settings = GlobalConfiguration.Instance;
        if (!settings.UpdateConfig.ShouldUpdateExtensions(true) || installed.Length == 0) return;
        try
        {
            // External app updates have no preparation step in the old process.
            if (NuGetVersion.TryParse(settings.LastStartedVersion, out var previous) && host > previous
                && !installed.Any(p => PackageInstaller.FindStagedExtensionUpdate(p, host, includeStandalone: false) != null))
            {
                await startup.GetTask<AuthenticationTask>().Task;
                await _manager.StageExtensionUpdatesAsync(host, appUpdate: true, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
        {
            _logger.LogWarning(ex, "Could not check for extension updates after the Beutl upgrade.");
        }
    }

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
