using System.Text.Json;
using Microsoft.Extensions.Logging;
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

        string deploymentId = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), $".data-install-{deploymentId}");
        Directory.CreateDirectory(staging);
        var changes = new List<PayloadChange>();
        try
        {
            Stage(MaterialsContentDirectory, BeutlEnvironment.GetMaterialsDirectoryPath(), material);
            Stage(TemplatesContentDirectory, BeutlEnvironment.GetTemplatesDirectoryPath(), template);
            token.ThrowIfCancellationRequested();
            return new DataPackageDeployment(this, name, package.Version, deploymentId, staging, changes, token);
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
            var change = new PayloadChange(kind, enabled, Path.Combine(root, name), staged, Path.Combine(staging, "backup-" + kind));
            if (!enabled)
            {
                // Removing the tag explicitly retires the package's old payload.
                changes.Add(change);
                return;
            }
            string source = Path.Combine(package.InstalledPath!, kind);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(source); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Omitting an enabled payload is a no-op, not an instruction to delete
                // existing data. An explicitly shipped empty directory still replaces it.
                _logger.LogWarning("Package {PackageName} ships no {ContentDirectory} directory; existing data is preserved.", name, kind);
                return;
            }
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException($"Package payload '{source}' is not a directory.");
            CopyDirectory(source, staged, token);
            token.ThrowIfCancellationRequested();
            WriteDurableJson(Path.Combine(staged, PayloadOwnerFileName), new PayloadOwner(name, package.Version, deploymentId));
            changes.Add(change);
        }
    }

    internal sealed class DataPackageDeployment(
        PackageInstaller owner, string name, string version, string deploymentId, string staging,
        List<PayloadChange> changes, CancellationToken token) : IDisposable
    {
        private bool _attempted;
        private bool _preserveBackup;
        private bool _disposed;

        // Cancellation is accepted until this short publication step starts. Once
        // it starts, callers finish repository registration without cancellation.
        public void Commit() => CommitCore(null);

        public void Commit(Action register)
        {
            ArgumentNullException.ThrowIfNull(register);
            CommitCore(register);
        }

        private void CommitCore(Action? register)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attempted) throw new InvalidOperationException("This deployment was already attempted.");
            _attempted = true;
            owner.TrackSyncOperation(() =>
            {
                lock (s_dataPackageGate)
                {
                    using FileStream installLock = AcquireDataInstallLock();
                    EnsureNoPendingDataInstall(name, staging);
                    token.ThrowIfCancellationRequested();
                    var active = new List<PayloadChange>();
                    foreach (PayloadChange change in changes)
                    {
                        PayloadOwnership ownership = owner.GetPayloadOwnership(name, change.Kind, change.Destination);
                        if (ownership == PayloadOwnership.Unknown)
                            throw new IOException($"Cannot determine ownership of the legacy package directory '{change.Destination}' without its installed metadata.");
                        bool owned = ownership == PayloadOwnership.Owned;
                        if (!change.Enabled && !owned) continue;
                        if (File.Exists(change.Destination))
                            throw new IOException($"The payload destination '{change.Destination}' is a file.");
                        if (Directory.Exists(change.Destination) && !owned)
                            throw new IOException($"The package cannot replace the unowned directory '{change.Destination}'.");
                        active.Add(change);
                    }
                    token.ThrowIfCancellationRequested();
                    var journal = CreateDataInstallJournal(name, version, deploymentId, active, register is not null);
                    foreach (PayloadChange change in active)
                        if (change.Enabled) RequirePublishedPayload(change.Staged, journal);
                    WriteDataInstallJournal(staging, journal);
                    _preserveBackup = true;
                    try
                    {
                        owner.AfterDataInstallStep?.Invoke("prepared");
                        foreach (PayloadChange change in active)
                        {
                            if (change.Enabled) RequirePublishedPayload(change.Staged, journal);
                            Directory.CreateDirectory(Path.GetDirectoryName(change.Destination)!);
                            if (Directory.Exists(change.Destination))
                            {
                                Directory.Move(change.Destination, change.Backup);
                                owner.AfterDataInstallStep?.Invoke("backup-" + change.Kind);
                            }
                            if (change.Enabled)
                            {
                                Directory.Move(change.Staged, change.Destination);
                                owner.AfterDataInstallStep?.Invoke("publish-" + change.Kind);
                            }
                        }
                        // Registration is part of the same transaction. Its failure restores
                        // replacements and removals while the previous backups still exist.
                        journal = journal with { Phase = DataInstallPhase.Published };
                        WriteDataInstallJournal(staging, journal);
                        owner.AfterDataInstallStep?.Invoke("published");
                        register?.Invoke();
                    }
                    catch (Exception failure)
                    {
                        try
                        {
                            journal = journal with { Phase = DataInstallPhase.RollingBack };
                            WriteDataInstallJournal(staging, journal);
                            RollBackDataInstall(staging, journal, owner.AfterDataInstallStep);
                            _preserveBackup = false;
                        }
                        catch (Exception rollback)
                        {
                            throw new AggregateException($"Package rollback failed; backups remain at '{staging}'.", failure, rollback);
                        }
                        throw;
                    }
                    // Registration has succeeded. If recording the final state fails, keep
                    // Published so startup can finish registration again, never undo its payload.
                    owner.AfterDataInstallStep?.Invoke("registered");
                    try
                    {
                        WriteDataInstallJournal(staging, journal with { Phase = DataInstallPhase.Committed });
                        owner.AfterDataInstallStep?.Invoke("committed");
                        _preserveBackup = false;
                    }
                    catch (Exception ex)
                    {
                        owner._logger.LogWarning(ex, "Package {PackageName} was published; completion will be retried from {Staging}.", name, staging);
                    }
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_preserveBackup)
            {
                if (!File.Exists(Path.Combine(staging, DataInstallJournalFileName))) owner.DeleteIfExists(staging);
                else
                {
                    try { DeleteRecoveredDataInstall(staging); }
                    catch (Exception ex) { owner._logger.LogWarning(ex, "Keeping completed package staging at {Staging} for cleanup.", staging); }
                }
            }
        }
    }

    internal sealed class PayloadChange(string kind, bool enabled, string destination, string staged, string backup)
    {
        public string Kind { get; } = kind;
        public bool Enabled { get; } = enabled;
        public string Destination { get; } = destination;
        public string Staged { get; } = staged;
        public string Backup { get; } = backup;
    }

    /// <summary>
    /// Removes the payload directories <see cref="InstallDataPackage"/> created.
    /// Returns <see langword="false"/> when removal fails or legacy ownership cannot be determined.
    /// </summary>
    /// <remarks>
    /// Owner markers remain sufficient when the extracted package is gone. Markerless legacy
    /// directories require installed metadata; missing metadata must not authorize deletion
    /// or let callers discard the package registration as if removal had succeeded.
    /// </remarks>
    public bool UninstallDataPackage(string packageName)
    {
        return TrackSyncOperation(() =>
        {
            lock (s_dataPackageGate)
            {
                using FileStream installLock = AcquireDataInstallLock();
                string name = ValidatePackageName(packageName);
                EnsureNoPendingDataInstall(name, null);
                string templatesPath = Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name);
                string materialsPath = Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name);
                bool templates = RemoveOwnedPayload(name, TemplatesContentDirectory, templatesPath);
                bool materials = RemoveOwnedPayload(name, MaterialsContentDirectory, materialsPath);
                return templates && materials;
            }
        });
    }

    private bool RemoveOwnedPayload(string packageName, string kind, string directory)
    {
        switch (GetPayloadOwnership(packageName, kind, directory))
        {
            case PayloadOwnership.Owned:
                return DeleteIfExists(directory);
            case PayloadOwnership.Unknown:
                _logger.LogWarning("Cannot determine ownership of {Directory} because installed package metadata is missing. Keeping the directory and package registration.", directory);
                return false;
            default:
                return true;
        }
    }

    private enum PayloadOwnership
    {
        Unowned,
        Owned,
        Unknown,
    }

    private sealed record PayloadOwner(string Name, string Version, string? DeploymentId = null);

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

    private PayloadOwnership GetPayloadOwnership(string packageName, string kind, string directory)
    {
        if (!Directory.Exists(directory))
            return PayloadOwnership.Unowned;
        PayloadOwner? owner = ReadPayloadOwner(directory, out bool markerPresent);
        if (markerPresent)
            return owner is not null && StringComparer.OrdinalIgnoreCase.Equals(owner.Name, packageName)
                ? PayloadOwnership.Owned : PayloadOwnership.Unowned;

        // Older installations had no marker. Only extracted, registered package metadata
        // can authorize taking over their payload; a matching directory name is insufficient.
        string tag = kind == MaterialsContentDirectory ? PackageKinds.MaterialTag : PackageKinds.TemplateTag;
        bool missingMetadata = false;
        foreach (var identity in _installedPackageRepository.GetLocalPackages(packageName))
        {
            string installed = Helper.ResolveInstalledDirectory(identity);
            if (!Directory.Exists(installed))
            {
                missingMetadata = true;
                continue;
            }
            using var reader = new PackageFolderReader(installed);
            if (new LocalPackage(reader.NuspecReader).Tags.Contains(tag))
                return PayloadOwnership.Owned;
        }
        return missingMetadata ? PayloadOwnership.Unknown : PayloadOwnership.Unowned;
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
            output.Flush(flushToDisk: true);
        }
    }
}
