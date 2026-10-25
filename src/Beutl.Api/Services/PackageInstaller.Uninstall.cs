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
        return TrackSyncOperation(() =>
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

            long size = MeasureInstalledSize(unnecessaryPackages);

            _logger.LogInformation("Prepared uninstall context. Uninstall package: {UninstallPackage}, Size to be released: {SizeToBeReleased}", uninstallPackage, size);

            return new PackageUninstallContext(uninstallPackage, installedPath)
            {
                UnnecessaryPackages = unnecessaryPackages,
                SizeToBeReleased = size
            };
        });
    }

    public void Uninstall(
        PackageUninstallContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        TrackSyncOperation(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_installedPackageRepository.RestoreFailed)
            {
                throw new InvalidOperationException(
                    "Cannot uninstall packages because installedPackages.json could not be restored.");
            }

            context.FailedPackages = RemovePackageDirectories(
                context.UnnecessaryPackages,
                context.SizeToBeReleased,
                progress,
                out _);
        });
    }
}
