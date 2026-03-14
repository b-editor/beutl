using System.Collections.Concurrent;

using Beutl.Api.Services;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NuGet.Common;
using NuGet.Packaging.Core;

namespace Beutl.Services.StartupTasks;

public sealed class ResolvePackageDependenciesTask : StartupTask
{
    private readonly ILogger<ResolvePackageDependenciesTask> _logger =
        Log.CreateLogger<ResolvePackageDependenciesTask>();

    public ResolvePackageDependenciesTask(
        InstalledPackageRepository repository,
        PackageInstaller installer)
    {
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("ResolvePackageDependenciesTask"))
            {
                PackageIdentity[] packages = repository.GetPackagesNeedingDependencyReResolution();

                if (packages.Length == 0)
                {
                    _logger.LogInformation(
                        "All installed packages were resolved under the current Beutl version. No re-resolution needed.");
                    return;
                }

                _logger.LogInformation(
                    "Beutl version changed. Re-resolving dependencies for {Count} package(s).",
                    packages.Length);

                foreach (PackageIdentity package in packages)
                {
                    try
                    {
                        activity?.AddEvent(new ActivityEvent(
                            $"Re-resolving {package.Id} {package.Version}"));

                        await installer.ReResolveDependencies(
                            package, null, CancellationToken.None);

                        repository.SetResolvedBeutlVersion(
                            package.Id, BeutlApplication.Version);

                        _logger.LogInformation(
                            "Successfully re-resolved dependencies for {PackageId} {PackageVersion}.",
                            package.Id, package.Version);
                    }
                    catch (Exception ex)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error);
                        _logger.LogError(ex,
                            "Failed to re-resolve dependencies for {PackageId} {PackageVersion}.",
                            package.Id, package.Version);
                        Failures.Add((package, ex));
                    }
                }
            }
        });
    }

    public override Task Task { get; }

    public ConcurrentBag<(PackageIdentity Package, Exception Error)> Failures { get; } = [];
}
