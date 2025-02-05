using Microsoft.Extensions.Logging;

using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    public PackageUninstallContext PrepareForUninstall(
        string installedPath,
        bool clean = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Preparing for uninstall. Installed path: {InstalledPath}, Clean: {Clean}", installedPath, clean);

        PackageIdentity uninstallPackage = new PackageFolderReader(installedPath).GetIdentity();
        PackageIdentity[] unnecessaryPackages = [uninstallPackage];

        if (clean)
        {
            _logger.LogInformation("Cleaning unnecessary packages.");

            PackageIdentity[] installedPackages = _installedPackageRepository.GetLocalPackages()
                .Except(unnecessaryPackages, PackageIdentityComparer.Default)
                .ToArray();

            unnecessaryPackages = UnnecessaryPackages(installedPackages);
        }

        long size = 0;
        foreach (PackageIdentity package in unnecessaryPackages)
        {
            string directory = Helper.PackagePathResolver.GetInstalledPath(package);
            foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }
        }

        _logger.LogInformation("Prepared uninstall context. Uninstall package: {UninstallPackage}, Size to be released: {SizeToBeReleased}", uninstallPackage, size);

        return new PackageUninstallContext(uninstallPackage, installedPath)
        {
            UnnecessaryPackages = unnecessaryPackages,
            SizeToBeReleased = size
        };
    }

    public void Uninstall(
        PackageUninstallContext context,
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
                    _logger.LogInformation("Deleted file: {FileName}", file);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete file: {FileName}", file);
                    hasAnyFailures = true;
                }

                progress.Report(totalSize / (double)context.SizeToBeReleased);
            }

            try
            {
                Directory.Delete(directory, true);
                _logger.LogInformation("Deleted directory: {Directory}", directory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete directory: {Directory}", directory);
                hasAnyFailures = true;
            }

            if (hasAnyFailures)
            {
                failedPackages.Add(directory);
                _logger.LogWarning("Package uninstallation had failures: {PackageId}", package.Id);
            }
            else
            {
                _logger.LogInformation("Successfully uninstalled package: {PackageId}", package.Id);
            }

            _installedPackageRepository.RemovePackage(package);
        }

        context.FailedPackages = failedPackages;
    }
}
