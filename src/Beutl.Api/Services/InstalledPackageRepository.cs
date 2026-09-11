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
    private const string FileName = "installedPackages.json";

    public InstalledPackageRepository()
    {
        Restore();
    }

    public IEnumerable<PackageIdentity> GetLocalPackages()
    {
        return _packages;
    }

    public IEnumerable<PackageIdentity> GetLocalPackages(string name)
    {
        return _packages.Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name));
    }

    public void UpgradePackages(PackageIdentity package)
        => UpgradePackagesAndDeferNotifications(package)();

    internal Action UpgradePackagesAndDeferNotifications(PackageIdentity package)
    {
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
                PublishCommittedChange(removed, false);
            PublishCommittedChange(package, true);
        };
    }

    private void PublishCommittedChange(PackageIdentity package, bool exists)
    {
        try
        {
            _subject.OnNext((package, exists));
        }
        catch (Exception ex)
        {
            // Observer failures cannot undo an already persisted registration or make
            // the installer roll back the payload while the repository records the new version.
            ReportObserverFailure(ex);
        }
    }

    private void ReportObserverFailure(Exception exception)
    {
        try { _logger.LogError(exception, "An installed-package observer failed while receiving a notification."); }
        catch { }
    }

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

    public void AddPackage(string name, string version)
    {
        _logger.LogInformation("Adding package: {PackageName} with version: {PackageVersion}", name, version);
        var package = new PackageIdentity(name, new NuGetVersion(version));
        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(package);
        if (!Directory.Exists(installedPath))
        {
            _logger.LogError("Directory not found for package: {PackageName} with version: {PackageVersion}", name, version);
            throw new DirectoryNotFoundException();
        }

        if (_packages.Add(package))
        {
            _resolvedBeutlVersions[package.Id] = BeutlApplication.Version;
            Save();
            _subject.OnNext((package, true));
        }

        _logger.LogInformation("Added package: {PackageName} with version: {PackageVersion}", name, version);
    }

    public void AddPackage(PackageIdentity package)
    {
        _logger.LogInformation("Adding package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(package);
        if (!Directory.Exists(installedPath))
        {
            _logger.LogError("Directory not found for package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
            throw new DirectoryNotFoundException();
        }

        if (_packages.Add(package))
        {
            _resolvedBeutlVersions[package.Id] = BeutlApplication.Version;
            Save();
            _subject.OnNext((package, true));
        }
        _logger.LogInformation("Added package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
    }

    public void RemovePackage(string name, string version)
    {
        _logger.LogInformation("Removing package: {PackageName} with version: {PackageVersion}", name, version);
        var nugetVersion = new NuGetVersion(version);
        PackageIdentity? package = _packages.FirstOrDefault(
            x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name) && x.Version == nugetVersion);
        if (package != null && _packages.Remove(package))
        {
            _resolvedBeutlVersions.Remove(name);
            Save();
            _subject.OnNext((package, false));
        }
        _logger.LogInformation("Removed package: {PackageName} with version: {PackageVersion}", name, version);
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

    public IObservable<bool> GetObservable(string name, string? version = null)
    {
        return new ObserverIsolatingObservable<bool>(new _Observable(this, name, version), ReportObserverFailure);
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
        _logger.LogInformation("Saving installed packages to file.");
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        JsonSerializer.SerializeToNode(packages.ToArray())!.JsonSave(fileName);
    }

    private void Restore()
    {
        _logger.LogInformation("Restoring installed packages from file.");
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        if (File.Exists(fileName))
        {
            using (FileStream stream = File.OpenRead(fileName))
            {
                try
                {
                    if (JsonSerializer.Deserialize<S_Package[]>(stream) is S_Package[] packages)
                    {
                        _packages.Clear();
                        _resolvedBeutlVersions.Clear();

                        _packages.AddRange(packages.Select(x => new PackageIdentity(x.Name, new NuGetVersion(x.Version))));

                        foreach (S_Package pkg in packages)
                        {
                            if (pkg.BeutlVersion is { } beutlVersion)
                            {
                                _resolvedBeutlVersions[pkg.Name] = beutlVersion;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore packages from file.");
                }
            }

            _logger.LogInformation("Restored {Count} packages from file.", _packages.Count);
        }
        else
        {
            _logger.LogWarning("No installed packages file found.");
        }
    }

    // Serializable
    private record S_Package(string Name, string Version, string? BeutlVersion = null);

    private sealed class _Observable : LightweightObservableBase<bool>
    {
        private readonly InstalledPackageRepository _repository;
        private readonly string _name;
        private readonly PackageIdentity? _packageIdentity;
        private IDisposable? _disposable;

        public _Observable(InstalledPackageRepository repository, string name, string? version)
        {
            _repository = repository;
            _name = name;

            if (version is { })
            {
                _packageIdentity = new PackageIdentity(name, new NuGetVersion(version));
            }
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            if (_packageIdentity is { })
            {
                observer.OnNext(_repository.ExistsPackage(_packageIdentity));
            }
            else
            {
                observer.OnNext(_repository.ExistsPackage(_name));
            }
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
            if (_packageIdentity != null)
            {
                if (PackageIdentity.Comparer.Equals(_packageIdentity, obj.Package))
                {
                    PublishNext(obj.Exists);
                }
            }
            else if (StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
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
