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

                PackageIdentity[] installedPackages = _installedPackageRepository.GetProfilePackages()
                    .Except(unnecessaryPackages, PackageIdentityComparer.Default)
                    .ToArray();

                unnecessaryPackages = UnnecessaryPackages(installedPackages);
            }

            long size = MeasureInstalledSize(unnecessaryPackages);

            _logger.LogInformation("Prepared uninstall context. Uninstall package: {UninstallPackage}, Size to be released: {SizeToBeReleased}", uninstallPackage, size);

            return new PackageUninstallContext(uninstallPackage, installedPath)
            {
                UnnecessaryPackages = unnecessaryPackages,
                SizeToBeReleased = size,
                RecheckDependencies = clean
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
            using var lease = PackageFileLock.Acquire(PackageFileChangesLockPath, cancellationToken);
            UninstallCore(context, progress, cancellationToken);
        });
    }

    // Callers on the UI thread await this instead of blocking while another editor changes package files.
    public Task UninstallAsync(
        PackageUninstallContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        return TrackSyncOperationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream lease = await PackageFileLock.AcquireAsync(PackageFileChangesLockPath, cancellationToken)
                .ConfigureAwait(false);
            UninstallCore(context, progress, cancellationToken);
        });
    }

    private void UninstallCore(
        PackageUninstallContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (!_installedPackageRepository.TryGetCompleteProfilePackages(out PackageIdentity[] installed))
        {
            throw new InvalidOperationException(
                "Cannot uninstall packages because installedPackages.json could not be restored.");
        }

        PackageIdentity[] unnecessary = context.RecheckDependencies
            ? [.. context.UnnecessaryPackages.Except(
                GetRequiredPackages(installed.Where(package => !package.Equals(context.Id))), PackageIdentityComparer.Default)]
            : context.UnnecessaryPackages;
        context.FailedPackages = RemovePackageDirectories(
            unnecessary,
            installed,
            context.SizeToBeReleased,
            progress,
            out _);
    }
}
