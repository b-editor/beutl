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

        PackageIdentity uninstallPackage = new PackageFolderReader(installedPath).GetIdentity();
        PackageIdentity[] unnecessaryPackages = [uninstallPackage];

        if (clean)
        {
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
            bool hasAnyFailtures = false;
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
                    _logger.LogError(ex, "Failed to remove files contained in package. (FileName: {FileName})", Path.GetFileName(file));
                    hasAnyFailtures = true;
                }

                progress.Report(totalSize / (double)context.SizeToBeReleased);
            }

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove package directory. (PackageId: {PackageId})", package.Id);
                hasAnyFailtures = true;
            }

            if (hasAnyFailtures)
            {
                failedPackages.Add(directory);
            }

            _installedPackageRepository.RemovePackage(package);
        }

        context.FailedPackages = failedPackages;
    }
}
