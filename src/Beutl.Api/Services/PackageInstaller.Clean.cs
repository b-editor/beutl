﻿using Microsoft.Extensions.Logging;

using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private PackageIdentity[] UnnecessaryPackages(IEnumerable<PackageIdentity>? installedPackages = null)
    {
        if (!Directory.Exists(Helper.InstallPath))
        {
            _logger.LogWarning("Install path does not exist: {InstallPath}", Helper.InstallPath);
            return Array.Empty<PackageIdentity>();
        }

        NuGetFramework framework = Helper.GetFrameworkName();

        var availablePackages = new HashSet<PackageDependencyInfo>(PackageIdentityComparer.Default);

        installedPackages ??= _installedPackageRepository.GetLocalPackages();

        foreach (PackageIdentity packageId in installedPackages)
        {
            string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
            if (directory != null)
            {
                var reader = new PackageFolderReader(directory);

                IEnumerable<PackageDependencyGroup> deps = reader.GetPackageDependencies();
                NuGetFramework? nearest = Helper.FrameworkReducer.GetNearest(
                    framework,
                    deps.Select(x => x.TargetFramework));

                Helper.GetPackageDependencies(
                    new PackageDependencyInfo(packageId, deps
                        .Where(x => x.TargetFramework == nearest)
                        .SelectMany(x => x.Packages)),
                    framework,
                    availablePackages);
            }
        }

        IEnumerable<PackageIdentity> all = Directory.GetDirectories(Helper.InstallPath)
            .Select(x => new PackageFolderReader(x).GetIdentity());

        return all.Except(availablePackages, PackageIdentityComparer.Default).ToArray();
    }

    public PackageCleanContext PrepareForClean(IEnumerable<PackageIdentity>? excludedPackages = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        excludedPackages ??= Enumerable.Empty<PackageIdentity>();

        PackageIdentity[] unnecessaryPackages = UnnecessaryPackages()
            .Except(excludedPackages, PackageIdentityComparer.Default)
            .ToArray();

        long size = 0;
        foreach (PackageIdentity package in unnecessaryPackages)
        {
            string directory = Helper.PackagePathResolver.GetInstalledPath(package);
            foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }
        }

        _logger.LogInformation("Prepared for clean. Unnecessary packages: {PackageCount}, Total size: {TotalSize} bytes", unnecessaryPackages.Length, size);

        return new PackageCleanContext(unnecessaryPackages, size);
    }

    public void Clean(
        PackageCleanContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failedPackages = new List<string>();
        long totalSize = 0;
        foreach (PackageIdentity package in context.UnnecessaryPackages)
        {
            string directory = Helper.PackagePathResolver.GetInstalledPath(package);
            bool hasAnyFailures = false;
            foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new FileInfo(file);
                    totalSize += fi.Length;
                    fi.Delete();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete file: {FileName} in package: {PackageId}", Path.GetFileName(file), package.Id);
                    hasAnyFailures = true;
                }

                progress.Report(totalSize / (double)context.SizeToBeReleased);
            }

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete package directory: {Directory} for package: {PackageId}", directory, package.Id);
                hasAnyFailures = true;
            }

            if (hasAnyFailures)
            {
                failedPackages.Add(directory);
            }

            _installedPackageRepository.RemovePackage(package);
        }

        context.FailedPackages = failedPackages;

        if (failedPackages.Count > 0)
        {
            _logger.LogWarning("Clean completed with failures. Failed packages: {FailedPackageCount}", failedPackages.Count);
        }
        else
        {
            _logger.LogInformation("Clean completed successfully. Total size released: {TotalSize} bytes", totalSize);
        }
    }
}
