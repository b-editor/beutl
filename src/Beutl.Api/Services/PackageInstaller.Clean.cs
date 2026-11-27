using Microsoft.Extensions.Logging;

using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private PackageIdentity[] UnnecessaryPackages(IEnumerable<PackageIdentity> installedPackages)
    {
        if (!Directory.Exists(Helper.InstallPath))
        {
            _logger.LogWarning("Install path does not exist: {InstallPath}", Helper.InstallPath);
            return [];
        }

        HashSet<PackageIdentity> needed = GetRequiredPackages(installedPackages);
        var unnecessary = new List<PackageIdentity>();
        foreach (string directory in Directory.GetDirectories(Helper.InstallPath))
        {
            using var reader = new PackageFolderReader(directory);
            PackageIdentity package = reader.GetIdentity();
            if (!needed.Contains(package)) unnecessary.Add(package);
        }
        return [.. unnecessary];
    }

    private static HashSet<PackageIdentity> GetRequiredPackages(IEnumerable<PackageIdentity> installedPackages)
    {
        NuGetFramework framework = Helper.GetFrameworkName();
        var needed = new HashSet<PackageIdentity>(PackageIdentityComparer.Default);
        foreach (PackageIdentity packageId in installedPackages)
        {
            needed.Add(packageId);
            // Must resolve like the deletion pass below: a package whose .nupkg is gone would
            // otherwise contribute no dependency metadata yet still be deletable, which would
            // classify a live dependency as unnecessary.
            string directory = Helper.ResolveInstalledDirectory(packageId);
            if (Directory.Exists(directory))
            {
                using var reader = new PackageFolderReader(directory);
                needed.UnionWith(ResolvedPackageDependencies.Load(reader, framework, preserveCandidates: true));
            }
        }

        return needed;
    }

    private long MeasureInstalledSize(IEnumerable<PackageIdentity> packages)
    {
        long size = 0;
        foreach (PackageIdentity package in packages)
        {
            string directory = Helper.ResolveInstalledDirectory(package);
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Installed directory not found for package: {PackageId}", package.Id);
                continue;
            }

            foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }
        }

        return size;
    }

    public PackageCleanContext PrepareForClean(IEnumerable<PackageIdentity>? excludedPackages = null, CancellationToken cancellationToken = default)
    {
        return TrackSyncOperation(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            excludedPackages ??= [];

            // An incomplete registry makes every extracted package and its data payload look unnecessary.
            if (IsRegistryIncompleteForClean(out PackageIdentity[] installed))
            {
                _logger.LogWarning(
                    "The installed package registry is empty or could not be restored; skipping clean to preserve extracted packages and their data.");
                return new PackageCleanContext([], 0) { RegistryUnavailable = true };
            }

            PackageIdentity[] unnecessaryPackages = UnnecessaryPackages(installed)
                .Except(excludedPackages, PackageIdentityComparer.Default)
                .ToArray();

            long size = MeasureInstalledSize(unnecessaryPackages);

            _logger.LogInformation("Prepared for clean. Unnecessary packages: {PackageCount}, Total size: {TotalSize} bytes", unnecessaryPackages.Length, size);

            return new PackageCleanContext(unnecessaryPackages, size) { RecheckDependencies = true };
        });
    }

    public void Clean(
        PackageCleanContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        TrackSyncOperation(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = PackageFileLock.Acquire(PackageFileChangesLockPath, cancellationToken);
            if (IsRegistryIncompleteForClean(out PackageIdentity[] installed))
            {
                throw new InvalidOperationException(
                    "Cannot clean packages because installedPackages.json is empty or could not be restored.");
            }

            PackageIdentity[] unnecessary = context.RecheckDependencies
                ? [.. context.UnnecessaryPackages.Except(GetRequiredPackages(installed), PackageIdentityComparer.Default)]
                : context.UnnecessaryPackages;
            List<string> failedPackages = RemovePackageDirectories(
                unnecessary,
                installed,
                context.SizeToBeReleased,
                progress,
                out long totalSize);
            context.FailedPackages = failedPackages;

            if (failedPackages.Count > 0)
            {
                _logger.LogWarning("Clean completed with failures. Failed packages: {FailedPackageCount}", failedPackages.Count);
            }
            else
            {
                _logger.LogInformation("Clean completed successfully. Total size released: {TotalSize} bytes", totalSize);
            }
        });
    }

    // An empty registry cannot prove that the extracted directories are orphaned. This is
    // separate from a legitimately empty file: saves stay enabled, but clean must not run.
    private bool IsRegistryIncompleteForClean(out PackageIdentity[] packages)
    {
        return !_installedPackageRepository.TryGetCompleteProfilePackages(out packages)
            || (packages.Length == 0
                && Directory.Exists(Helper.InstallPath)
                && Directory.EnumerateDirectories(Helper.InstallPath).Any());
    }

    // Returns the directories that could not be removed completely.
    private List<string> RemovePackageDirectories(
        IReadOnlyCollection<PackageIdentity> packages,
        IReadOnlyCollection<PackageIdentity> installedPackages,
        long sizeToBeReleased,
        IProgress<double> progress,
        out long totalSize)
    {
        var failedPackages = new List<string>();
        totalSize = 0;
        foreach (PackageIdentity package in packages)
        {
            // A data package's payload lives outside the install directory, and it has to
            // go even when the extracted package itself is already missing. The payload
            // directory is keyed by id alone, so an update that leaves a newer identity
            // installed still owns it — removing it here would strip the live version.
            bool dataRemoved = TryRemovePackagePayload(packages, installedPackages, package);

            string directory = Helper.ResolveInstalledDirectory(package);
            if (!dataRemoved)
            {
                // PrepareForClean rediscovers candidates from extracted package directories,
                // so dropping this one would strand the payload with nothing left to retry
                // from. Keep both the directory and the repository entry for the next run.
                _logger.LogError("Failed to delete the data payload of package: {PackageId}", package.Id);
                failedPackages.Add(directory);
                continue;
            }

            ClearStagedExtensionUpdates(package);
            if (!Directory.Exists(directory))
            {
                // The files are already gone, so the repository entry would outlive them.
                _logger.LogWarning("Installed directory not found for package: {PackageId}", package.Id);
                _installedPackageRepository.RemovePackage(package);
                continue;
            }

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

                progress.Report(totalSize / (double)sizeToBeReleased);
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

        return failedPackages;
    }

    // False means repair/removal failed, not that no survivor exists. Do not turn a
    // failed restoration into an unconditional deletion of the surviving payload.
    private bool TryRemovePackagePayload(IReadOnlyCollection<PackageIdentity> removedPackages,
        IReadOnlyCollection<PackageIdentity> installedPackages, PackageIdentity package)
    {
        try
        {
            PackageIdentity? survivor = installedPackages
                .Where(other => StringComparer.OrdinalIgnoreCase.Equals(other.Id, package.Id)
                    && !other.Equals(package) && !removedPackages.Contains(other))
                .OrderByDescending(other => other.Version)
                .FirstOrDefault(other => Directory.Exists(Helper.ResolveInstalledDirectory(other)));
            if (survivor is null)
                return UninstallDataPackage(package.Id);

            string[] roots = [BeutlEnvironment.GetMaterialsDirectoryPath(), BeutlEnvironment.GetTemplatesDirectoryPath()];
            if (roots.Select(root => ReadPayloadOwner(Path.Combine(root, package.Id)))
                .Any(owner => owner is not null
                    && StringComparer.OrdinalIgnoreCase.Equals(owner.Name, package.Id)
                    && NuGetVersion.TryParse(owner.Version, out NuGetVersion? ownerVersion)
                    && ownerVersion == package.Version))
            {
                string installed = Helper.ResolveInstalledDirectory(survivor);
                using var reader = new PackageFolderReader(installed);
                var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = installed };
                if (localPackage.Tags.GetPackageKind() == PackageKind.Extension)
                    return UninstallDataPackage(package.Id);
                InstallDataPackage(localPackage);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repair or remove the payload for package {PackageId}.", package.Id);
            return false;
        }
    }
}
