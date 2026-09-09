using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private static readonly object s_dataPackageGate = new();

    private const string MaterialsContentDirectory = "materials";

    private const string TemplatesContentDirectory = "templates";

    /// <summary>
    /// Copies the payload a material or template package ships into the directory the
    /// editor reads it from. The nupkg itself stays extracted under the install path so
    /// uninstall and cleanup keep working the same way they do for extensions.
    /// </summary>
    public void InstallDataPackage(LocalPackage package)
    {
        TrackSyncOperation(() =>
        {
            if (string.IsNullOrEmpty(package.InstalledPath))
            {
                throw new ArgumentException(
                    $"'{package.Name}' has not been extracted yet.",
                    nameof(package));
            }

            string name = ValidatePackageName(package.Name);
            bool hasMaterial = package.Tags.Contains(PackageKinds.MaterialTag);
            bool hasTemplate = package.Tags.Contains(PackageKinds.TemplateTag);
            if (!hasMaterial && !hasTemplate)
            {
                throw new ArgumentException(
                    $"'{package.Name}' is an extension package and has no data payload.",
                    nameof(package));
            }

            lock (s_dataPackageGate)
            {
                InstallPayloads(package, name, hasMaterial, hasTemplate);
            }
        });
    }

    private void InstallPayloads(LocalPackage package, string name, bool hasMaterial, bool hasTemplate)
    {
        // Keep staging outside the watched material/template directories.
        string staging = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), $".data-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var changes = new List<PayloadChange>();
        bool preserveBackup = false;
        try
        {
            Stage(MaterialsContentDirectory, BeutlEnvironment.GetMaterialsDirectoryPath(), hasMaterial);
            Stage(TemplatesContentDirectory, BeutlEnvironment.GetTemplatesDirectoryPath(), hasTemplate);
            try
            {
                foreach (PayloadChange change in changes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(change.Destination)!);
                    if (Directory.Exists(change.Destination))
                    {
                        Directory.Move(change.Destination, change.Backup);
                        change.BackedUp = true;
                    }
                    if (Directory.Exists(change.Staged))
                    {
                        Directory.Move(change.Staged, change.Destination);
                        change.Published = true;
                    }
                }
            }
            catch (Exception failure)
            {
                List<Exception> failures = [failure];
                foreach (PayloadChange change in changes.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (change.Published)
                            Directory.Delete(change.Destination, recursive: true);
                        if (change.BackedUp)
                            Directory.Move(change.Backup, change.Destination);
                    }
                    catch (Exception rollbackFailure)
                    {
                        preserveBackup = true;
                        failures.Add(rollbackFailure);
                    }
                }
                if (preserveBackup)
                    throw new AggregateException($"Package rollback failed; backups remain at '{staging}'.", failures);
                throw;
            }
        }
        finally
        {
            if (!preserveBackup)
                DeleteIfExists(staging);
        }

        void Stage(string kind, string root, bool enabled)
        {
            string staged = Path.Combine(staging, kind);
            changes.Add(new PayloadChange(Path.Combine(root, name), staged, Path.Combine(staging, "backup-" + kind)));
            if (!enabled)
                return;
            string source = Path.Combine(package.InstalledPath!, kind);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(source);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _logger.LogWarning("Package {PackageName} ships no {ContentDirectory} directory.", package.Name, kind);
                return;
            }
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException($"Package payload '{source}' is not a directory.");
            CopyDirectory(source, staged);
        }
    }

    private sealed class PayloadChange(string destination, string staged, string backup)
    {
        public string Destination { get; } = destination;
        public string Staged { get; } = staged;
        public string Backup { get; } = backup;
        public bool BackedUp { get; set; }
        public bool Published { get; set; }
    }

    /// <summary>
    /// Removes the payload directories <see cref="InstallDataPackage"/> created.
    /// Returns <see langword="false"/> when something was left behind.
    /// </summary>
    /// <remarks>
    /// Both candidates are removed without consulting the nuspec: uninstall also runs when
    /// the extracted package is already gone, and the directory a package never created
    /// simply is not there.
    /// </remarks>
    public bool UninstallDataPackage(string packageName)
    {
        return TrackSyncOperation(() =>
        {
            lock (s_dataPackageGate)
            {
                string name = ValidatePackageName(packageName);
                bool templates = DeleteIfExists(Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name));
                bool materials = DeleteIfExists(Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name));
                return templates && materials;
            }
        });
    }

    // The name is a NuGet id read out of a downloaded nuspec, and it becomes a directory
    // this class also deletes; nothing else stops it from pointing outside the home.
    private static string ValidatePackageName(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)
            || packageName is "." or ".."
            || packageName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || packageName != Path.GetFileName(packageName))
        {
            throw new ArgumentException($"'{packageName}' is not a usable directory name.", nameof(packageName));
        }

        return packageName;
    }

    private bool DeleteIfExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete the package data directory {Directory}.", directory);
            return false;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
