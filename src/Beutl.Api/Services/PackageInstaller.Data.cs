using Microsoft.Extensions.Logging;
using System.Text.Json;
using NuGet.Packaging;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private static readonly object s_dataPackageGate = new();

    private const string PayloadOwnerFileName = ".beutl-package-owner";

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
            using var deployment = PrepareDataPackageCore(package, CancellationToken.None, requireData: true);
            deployment.Commit();
        });
    }

    internal DataPackageDeployment PrepareDataPackage(LocalPackage package, CancellationToken cancellationToken)
        => TrackSyncOperation(() => PrepareDataPackageCore(package, cancellationToken, requireData: false));

    private DataPackageDeployment PrepareDataPackageCore(LocalPackage package, CancellationToken token, bool requireData)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(package.InstalledPath))
            throw new ArgumentException($"'{package.Name}' has not been extracted yet.", nameof(package));
        string name = ValidatePackageName(package.Name);
        bool material = package.Tags.Contains(PackageKinds.MaterialTag);
        bool template = package.Tags.Contains(PackageKinds.TemplateTag);
        if (requireData && !material && !template)
            throw new ArgumentException($"'{package.Name}' is an extension package and has no data payload.", nameof(package));

        string staging = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), $".data-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var changes = new List<PayloadChange>();
        try
        {
            Stage(MaterialsContentDirectory, BeutlEnvironment.GetMaterialsDirectoryPath(), material);
            Stage(TemplatesContentDirectory, BeutlEnvironment.GetTemplatesDirectoryPath(), template);
            token.ThrowIfCancellationRequested();
            return new DataPackageDeployment(this, name, staging, changes, token);
        }
        catch
        {
            DeleteIfExists(staging);
            throw;
        }

        void Stage(string kind, string root, bool enabled)
        {
            token.ThrowIfCancellationRequested();
            string staged = Path.Combine(staging, kind);
            changes.Add(new PayloadChange(kind, enabled, Path.Combine(root, name), staged, Path.Combine(staging, "backup-" + kind)));
            if (!enabled) return;
            string source = Path.Combine(package.InstalledPath!, kind);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(source); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _logger.LogWarning("Package {PackageName} ships no {ContentDirectory} directory.", name, kind);
                return;
            }
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException($"Package payload '{source}' is not a directory.");
            CopyDirectory(source, staged, token);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staged, PayloadOwnerFileName), JsonSerializer.Serialize(new PayloadOwner(name, package.Version)));
        }
    }

    internal sealed class DataPackageDeployment(
        PackageInstaller owner, string name, string staging, List<PayloadChange> changes, CancellationToken token) : IDisposable
    {
        private bool _attempted;
        private bool _preserveBackup;
        private bool _disposed;

        // Cancellation is accepted until this short publication step starts. Once
        // it starts, callers finish repository registration without cancellation.
        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attempted) throw new InvalidOperationException("This deployment was already attempted.");
            _attempted = true;
            owner.TrackSyncOperation(() =>
            {
                lock (s_dataPackageGate)
                {
                    token.ThrowIfCancellationRequested();
                    var active = new List<PayloadChange>();
                    foreach (PayloadChange change in changes)
                    {
                        bool owned = owner.OwnsPayload(name, change.Kind, change.Destination);
                        if (!change.Enabled && !owned) continue;
                        if (Directory.Exists(change.Destination) && !owned)
                            throw new IOException($"The package cannot replace the unowned directory '{change.Destination}'.");
                        active.Add(change);
                    }
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (PayloadChange change in active)
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
                        foreach (PayloadChange change in active.AsEnumerable().Reverse())
                        {
                            try
                            {
                                if (change.Published) Directory.Move(change.Destination, change.Staged);
                                if (change.BackedUp) Directory.Move(change.Backup, change.Destination);
                            }
                            catch (Exception rollback)
                            {
                                _preserveBackup = true;
                                failures.Add(rollback);
                            }
                        }
                        if (_preserveBackup)
                            throw new AggregateException($"Package rollback failed; backups remain at '{staging}'.", failures);
                        throw;
                    }
                }
            });
        }

        public void PreserveBackup()
        {
            if (changes.Any(change => change.BackedUp))
            {
                _preserveBackup = true;
                owner._logger.LogWarning("Package registration failed; previous payload backups remain at {Staging}.", staging);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_preserveBackup) owner.DeleteIfExists(staging);
        }
    }

    internal sealed class PayloadChange(string kind, bool enabled, string destination, string staged, string backup)
    {
        public string Kind { get; } = kind;
        public bool Enabled { get; } = enabled;
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
                string templatesPath = Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name);
                string materialsPath = Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name);
                bool templates = !OwnsPayload(name, TemplatesContentDirectory, templatesPath) || DeleteIfExists(templatesPath);
                bool materials = !OwnsPayload(name, MaterialsContentDirectory, materialsPath) || DeleteIfExists(materialsPath);
                return templates && materials;
            }
        });
    }

    private sealed record PayloadOwner(string Name, string Version);

    private static PayloadOwner? ReadPayloadOwner(string directory)
        => ReadPayloadOwner(directory, out _);

    private static PayloadOwner? ReadPayloadOwner(string directory, out bool markerPresent)
    {
        string marker = Path.Combine(directory, PayloadOwnerFileName);
        markerPresent = true;
        try
        {
            if ((File.GetAttributes(marker) & FileAttributes.Directory) != 0)
                return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            markerPresent = false;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            PayloadOwner? owner = JsonSerializer.Deserialize<PayloadOwner>(File.ReadAllText(marker));
            return owner is not null && !string.IsNullOrWhiteSpace(owner.Name)
                && NuGet.Versioning.NuGetVersion.TryParse(owner.Version, out _) ? owner : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An existing unreadable marker is not proof of legacy ownership.
            return null;
        }
    }

    private bool OwnsPayload(string packageName, string kind, string directory)
    {
        if (!Directory.Exists(directory))
            return false;
        PayloadOwner? owner = ReadPayloadOwner(directory, out bool markerPresent);
        if (markerPresent)
            return owner is not null && StringComparer.OrdinalIgnoreCase.Equals(owner.Name, packageName);

        // Older installations had no marker. Only extracted, registered package metadata
        // can authorize taking over their payload; a matching directory name is insufficient.
        string tag = kind == MaterialsContentDirectory ? PackageKinds.MaterialTag : PackageKinds.TemplateTag;
        foreach (var identity in _installedPackageRepository.GetLocalPackages(packageName))
        {
            string installed = Helper.ResolveInstalledDirectory(identity);
            if (!Directory.Exists(installed))
                continue;
            using var reader = new PackageFolderReader(installed);
            if (new LocalPackage(reader.NuspecReader).Tags.Contains(tag))
                return true;
        }
        return false;
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

    private static void CopyDirectory(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = File.OpenRead(file);
            using var output = File.Create(target);
            input.CopyToAsync(output, token).GetAwaiter().GetResult();
        }
    }
}
