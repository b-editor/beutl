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
    {
        _logger.LogInformation("Upgrading package: {PackageId} to version: {PackageVersion}", package.Id, package.Version);
        PackageIdentity[] removedItems = [];
        if (_subject.HasObservers)
        {
            removedItems = GetLocalPackages(package.Id).ToArray();
        }
        _packages.RemoveWhere(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, package.Id));
        _packages.Add(package);
        Save();

        foreach (PackageIdentity removed in removedItems)
        {
            _subject.OnNext((removed, false));
        }

        _subject.OnNext((package, true));
        _logger.LogInformation("Upgraded package: {PackageId} to version: {PackageVersion}", package.Id, package.Version);
    }

    public void AddPackage(string name, string version)
    {
        _logger.LogInformation("Adding package: {PackageName} with version: {PackageVersion}", name, version);
        var package = new PackageIdentity(name, new NuGetVersion(version));
        string installedPath = Helper.PackagePathResolver.GetInstalledPath(package);
        if (!Directory.Exists(installedPath))
        {
            _logger.LogError("Directory not found for package: {PackageName} with version: {PackageVersion}", name, version);
            throw new DirectoryNotFoundException();
        }

        if (_packages.Add(package))
        {
            Save();
            _subject.OnNext((package, true));
        }

        _logger.LogInformation("Added package: {PackageName} with version: {PackageVersion}", name, version);
    }

    public void AddPackage(PackageIdentity package)
    {
        _logger.LogInformation("Adding package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
        string installedPath = Helper.PackagePathResolver.GetInstalledPath(package);
        if (!Directory.Exists(installedPath))
        {
            _logger.LogError("Directory not found for package: {PackageId} with version: {PackageVersion}", package.Id, package.Version);
            throw new DirectoryNotFoundException();
        }

        if (_packages.Add(package))
        {
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
        return new _Observable(this, name, version);
    }

    private void Save()
    {
        _logger.LogInformation("Saving installed packages to file.");
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        using (FileStream stream = File.Create(fileName))
        {
            JsonSerializer.Serialize(stream, _packages
                .Select(x => new S_Package(x.Id, x.Version.ToString()))
                .ToArray());
        }
        _logger.LogInformation("Saved {Count} packages to file.", _packages.Count);
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

                        _packages.AddRange(packages.Select(x => new PackageIdentity(x.Name, new NuGetVersion(x.Version))));
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
    private record S_Package(string Name, string Version);

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
            if ((_packageIdentity != null && _packageIdentity == obj.Package)
                || StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                PublishNext(obj.Exists);
            }
        }
    }
}
