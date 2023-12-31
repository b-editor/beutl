using System.Reactive.Subjects;
using System.Text.Json;

using Beutl.Reactive;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class InstalledPackageRepository : IBeutlApiResource
{
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
    }

    public void AddPackage(string name, string version)
    {
        var package = new PackageIdentity(name, new NuGetVersion(version));
        string installedPath = Helper.PackagePathResolver.GetInstalledPath(package);
        if (!Directory.Exists(installedPath))
            throw new DirectoryNotFoundException();

        if (_packages.Add(package))
        {
            Save();
            _subject.OnNext((package, true));
        }
    }

    public void AddPackage(PackageIdentity package)
    {
        string installedPath = Helper.PackagePathResolver.GetInstalledPath(package);
        if (!Directory.Exists(installedPath))
            throw new DirectoryNotFoundException();

        if (_packages.Add(package))
        {
            Save();
            _subject.OnNext((package, true));
        }
    }

    public void RemovePackage(string name, string version)
    {
        var nugetVersion = new NuGetVersion(version);
        PackageIdentity? package = _packages.FirstOrDefault(
            x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name) && x.Version == nugetVersion);
        if (package != null && _packages.Remove(package))
        {
            Save();
            _subject.OnNext((package, false));
        }
    }

    public void RemovePackage(PackageIdentity package)
    {
        if (_packages.Remove(package))
        {
            Save();
            _subject.OnNext((package, false));
        }
    }

    public void RemovePackages(string name)
    {
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
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        using (FileStream stream = File.Create(fileName))
        {
            JsonSerializer.Serialize(stream, _packages
                .Select(x => new S_Package(x.Id, x.Version.ToString()))
                .ToArray());
        }
    }

    private void Restore()
    {
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
                catch
                {

                }
            }
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
