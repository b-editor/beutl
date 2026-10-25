using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json;

using Beutl.Logging;
using Beutl.Reactive;

using Microsoft.Extensions.Logging;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class InstalledPackageRepository : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<InstalledPackageRepository>();
    private readonly HashSet<PackageIdentity> _packages = [];
    private readonly Dictionary<string, string?> _resolvedBeutlVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Subject<(PackageIdentity Package, bool Exists)> _subject = new();
    private bool _saveBlocked;
    private const string FileName = "installedPackages.json";

    public InstalledPackageRepository()
    {
        Restore();
    }

    /// <summary>
    /// True when the last restore could not read the file or skipped an entry. Saving is
    /// blocked so the file on disk is not replaced by the incomplete in-memory registry.
    /// </summary>
    public bool RestoreFailed => _saveBlocked;

    public IEnumerable<PackageIdentity> GetLocalPackages()
    {
        return _packages;
    }

    public IEnumerable<PackageIdentity> GetLocalPackages(string name)
    {
        return _packages.Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name));
    }

    internal Action UpgradePackagesAndDeferNotifications(PackageIdentity package)
    {
        if (_saveBlocked)
        {
            throw new InvalidOperationException(
                "Cannot register an installed package because installedPackages.json could not be restored.");
        }

        _logger.LogInformation("Upgrading package: {PackageId} to version: {PackageVersion}", package.Id, package.Version);
        PackageIdentity[] removedItems = [];
        if (_subject.HasObservers)
        {
            removedItems = GetLocalPackages(package.Id).ToArray();
        }
        PackageIdentity[] upgraded = [.. _packages.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Id, package.Id)), package];
        // Persist a candidate snapshot before publishing the new registration in memory.
        Save(upgraded.Select(x => new S_Package(x.Id, x.Version.ToString(),
            StringComparer.OrdinalIgnoreCase.Equals(x.Id, package.Id)
                ? BeutlApplication.Version : _resolvedBeutlVersions.GetValueOrDefault(x.Id))));
        _packages.RemoveWhere(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, package.Id));
        _packages.Add(package);
        _resolvedBeutlVersions[package.Id] = BeutlApplication.Version;

        return () =>
        {
            foreach (PackageIdentity removed in removedItems)
                _subject.OnNext((removed, false));
            _subject.OnNext((package, true));
        };
    }

    private void ReportObserverFailure(Exception exception)
        => _logger.LogError(exception, "An installed-package observer failed while receiving a notification.");

    // Wrap each subscription, not only the subject: one returned observable can
    // itself have several observers through LightweightObservableBase.PublishNext.
    private sealed class ObserverIsolatingObservable<T>(IObservable<T> source, Action<Exception> failure) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return source.Subscribe(Observer.Create<T>(
                value => Forward(() => observer.OnNext(value)),
                error => Forward(() => observer.OnError(error)),
                () => Forward(observer.OnCompleted)));

            void Forward(Action notification)
            {
                try { notification(); }
                catch (Exception ex) { failure(ex); }
            }
        }
    }

    public void RemovePackage(PackageIdentity package)
    {
        _logger.LogInformation("Removing package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
        if (_packages.Remove(package))
        {
            _resolvedBeutlVersions.Remove(package.Id);
            Save();
            _subject.OnNext((package, false));
        }
        _logger.LogInformation("Removed package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
    }

    public void RemovePackages(string name)
    {
        _logger.LogInformation("Removing all packages with name: {PackageName}", name);
        PackageIdentity[] removed = [];
        if (_subject.HasObservers)
        {
            removed = GetLocalPackages(name).ToArray();
        }
        _packages.RemoveWhere(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name));
        _resolvedBeutlVersions.Remove(name);
        Save();
        foreach (PackageIdentity package in removed)
        {
            _subject.OnNext((package, false));
        }
        _logger.LogInformation("Removed {Count} packages with name: {PackageName}", removed.Length, name);
    }

    public bool ExistsPackage(PackageIdentity package)
    {
        return _packages.Contains(package);
    }

    public bool ExistsPackage(string name, string version)
    {
        var nugetVersion = new NuGetVersion(version);
        return _packages.Any(
            x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name) && x.Version == nugetVersion);
    }

    public bool ExistsPackage(string name)
    {
        return _packages.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name));
    }

    public IObservable<bool> GetObservable(string name)
    {
        return new ObserverIsolatingObservable<bool>(new _Observable(this, name), ReportObserverFailure);
    }

    public IObservable<PackageIdentity?> GetPackageObservable(string name)
    {
        return new ObserverIsolatingObservable<PackageIdentity?>(new _PackageObservable(this, name), ReportObserverFailure);
    }

    public PackageIdentity[] GetPackagesNeedingDependencyReResolution()
    {
        string currentVersion = BeutlApplication.Version;
        return [.. _packages.Where(p =>
        {
            _resolvedBeutlVersions.TryGetValue(p.Id, out string? ver);
            return ver != currentVersion;
        })];
    }

    public void SetResolvedBeutlVersion(string packageId, string beutlVersion)
    {
        _resolvedBeutlVersions[packageId] = beutlVersion;
        Save();
    }

    private void Save()
        => Save(_packages.Select(x => new S_Package(x.Id, x.Version.ToString(), _resolvedBeutlVersions.GetValueOrDefault(x.Id))));

    private void Save(IEnumerable<S_Package> packages)
    {
        if (_saveBlocked)
        {
            _logger.LogWarning(
                "Skipping save of installed packages because installedPackages.json could not be restored.");
            return;
        }

        _logger.LogInformation("Saving installed packages to file.");
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        JsonSerializer.SerializeToNode(packages.ToArray())!.JsonSave(fileName);
    }

    private void Restore()
    {
        _logger.LogInformation("Restoring installed packages from file.");
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        if (!File.Exists(fileName))
        {
            _logger.LogWarning("No installed packages file found.");
            return;
        }

        int skipped = 0;
        _packages.Clear();
        _resolvedBeutlVersions.Clear();
        try
        {
            using FileStream stream = File.OpenRead(fileName);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("The installed packages file does not contain an array.");

            int index = -1;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                index++;
                try
                {
                    S_Package? pkg = element.Deserialize<S_Package>();
                    if (pkg is null)
                    {
                        skipped++;
                        _logger.LogWarning("Skipping a null installed package entry at index {Index}.", index);
                        continue;
                    }

                    _packages.Add(new PackageIdentity(pkg.Name, new NuGetVersion(pkg.Version)));
                    if (pkg.BeutlVersion is { } beutlVersion)
                    {
                        _resolvedBeutlVersions[pkg.Name] = beutlVersion;
                    }
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
                {
                    skipped++;
                    _logger.LogWarning(
                        ex,
                        "Skipping invalid installed package entry at index {Index}.", index);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Failed to restore packages from file; keeping the file unchanged.");
            KeepBackup(fileName);
            _saveBlocked = true;
            return;
        }

        if (skipped > 0)
        {
            // Keep the incomplete registry unchanged so the protection remains active
            // after the next start, including for clean.
            KeepBackup(fileName);
            _saveBlocked = true;
        }

        _logger.LogInformation(
            "Restored {Count} packages from file ({Skipped} invalid entries skipped).",
            _packages.Count,
            skipped);
    }

    private string? KeepBackup(string fileName)
    {
        string backup = fileName + ".bak";
        try
        {
            File.Copy(fileName, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not keep a backup of installedPackages.json.");
            return null;
        }
    }

    // Serializable
    private record S_Package(string Name, string Version, string? BeutlVersion = null);

    private sealed class _Observable : LightweightObservableBase<bool>
    {
        private readonly InstalledPackageRepository _repository;
        private readonly string _name;
        private IDisposable? _disposable;

        public _Observable(InstalledPackageRepository repository, string name)
        {
            _repository = repository;
            _name = name;
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            observer.OnNext(_repository.ExistsPackage(_name));
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;
        }

        protected override void Initialize()
        {
            _disposable = _repository._subject
                .Subscribe(OnReceived);
        }

        private void OnReceived((PackageIdentity Package, bool Exists) obj)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                // Per-event Exists is unreliable for name-only observers; re-evaluate the aggregate.
                PublishNext(_repository.ExistsPackage(_name));
            }
        }
    }

    private sealed class _PackageObservable : LightweightObservableBase<PackageIdentity?>
    {
        private readonly InstalledPackageRepository _repository;
        private readonly string _name;
        private IDisposable? _disposable;

        public _PackageObservable(InstalledPackageRepository repository, string name)
        {
            _repository = repository;
            _name = name;
        }

        protected override void Subscribed(IObserver<PackageIdentity?> observer, bool first)
        {
            observer.OnNext(GetLatestInstalled());
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;
        }

        protected override void Initialize()
        {
            _disposable = _repository._subject.Subscribe(OnReceived);
        }

        private void OnReceived((PackageIdentity Package, bool Exists) obj)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                PublishNext(GetLatestInstalled());
            }
        }

        private PackageIdentity? GetLatestInstalled()
        {
            return _repository.GetLocalPackages(_name)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
        }
    }
}
