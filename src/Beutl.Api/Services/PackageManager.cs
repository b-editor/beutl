using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reactive.Subjects;
using System.Reflection;

using Beutl.Api.Objects;
using Beutl.Extensibility;
using Beutl.Reactive;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public sealed class PackageManager : PackageLoader
{
    private readonly ConcurrentBag<LocalPackage> _loadedPackage = new();
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly BeutlApiApplication _apiApplication;
    private readonly Subject<(PackageIdentity Package, bool Loaded)> _subject = new();

    public PackageManager(
        InstalledPackageRepository installedPackageRepository,
        BeutlApiApplication apiApplication)
    {
        ExtensionProvider = new();
        _installedPackageRepository = installedPackageRepository;
        _apiApplication = apiApplication;
    }

    public IEnumerable<LocalPackage> LoadedPackage => _loadedPackage;

    public ExtensionProvider ExtensionProvider { get; }

    public IObservable<bool> GetObservable(string name, string? version = null)
    {
        return new _Observable(this, name, version);
    }

    public bool IsLoaded(string name, string? version = null)
    {
        if (version is { })
        {
            var nugetVersion = new NuGetVersion(version);
            return _loadedPackage.Any(
                x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, name)
                    && new NuGetVersion(x.Version) == nugetVersion);
        }
        else
        {
            return _loadedPackage.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, name));
        }
    }

    public IReadOnlyList<LocalPackage> GetLocalSourcePackages()
    {
        string[] files = Directory.GetFiles(Helper.LocalSourcePath, "*.nupkg");
        var list = new List<LocalPackage>(files.Length);

        foreach (string file in files)
        {
            using FileStream stream = File.OpenRead(file);
            if (Helper.ReadLocalPackageFromNupkgFile(stream) is { } localPackage)
            {
                if (!_loadedPackage.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, localPackage.Name)))
                {
                    list.Add(localPackage);
                }
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<PackageUpdate>> CheckUpdate()
    {
        PackageIdentity[] packages = _installedPackageRepository.GetLocalPackages().ToArray();

        var updates = new List<PackageUpdate>(packages.Length);
        DiscoverService discover = _apiApplication.GetResource<DiscoverService>();

        for (int i = 0; i < packages.Length; i++)
        {
            PackageIdentity pkg = packages[i];
            NuGetVersion version = pkg.Version;
            string versionStr = version.ToString();
            try
            {
                Package remotePackage = await discover.GetPackage(pkg.Id).ConfigureAwait(false);

                foreach (Release? item in await remotePackage.GetReleasesAsync().ConfigureAwait(false))
                {
                    // 降順
                    if (new NuGetVersion(item.Version.Value).CompareTo(version) > 0)
                    {
                        Release? oldRelease = await Helper.TryGetOrDefault(() => remotePackage.GetReleaseAsync(versionStr))
                            .ConfigureAwait(false);
                        updates.Add(new PackageUpdate(remotePackage, oldRelease, item));
                        break;
                    }
                }
            }
            catch
            {

            }
        }

        return updates;
    }

    public async Task<PackageUpdate?> CheckUpdate(string name)
    {
        DiscoverService discover = _apiApplication.GetResource<DiscoverService>();

        LocalPackage? pkg = _loadedPackage.FirstOrDefault(v => !v.SideLoad && StringComparer.OrdinalIgnoreCase.Equals(v.Name, name));
        if (pkg != null)
        {
            string versionStr = pkg.Version;
            var version = new NuGetVersion(versionStr);
            Package remotePackage = await discover.GetPackage(pkg.Name).ConfigureAwait(false);

            foreach (Release? item in await remotePackage.GetReleasesAsync().ConfigureAwait(false))
            {
                // 降順
                if (new NuGetVersion(item.Version.Value).CompareTo(version) > 0)
                {
                    Release? oldRelease = await Helper.TryGetOrDefault(() => remotePackage.GetReleaseAsync(pkg.Version))
                        .ConfigureAwait(false);
                    return new PackageUpdate(remotePackage, oldRelease, item);
                }
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<LocalPackage>> GetPackages()
    {
        async Task<Package?> GetPackage(string id)
        {
            try
            {
                PackageResponse package = await _apiApplication.Packages.GetPackageAsync(id).ConfigureAwait(false);
                ProfileResponse profile = await _apiApplication.Users.GetUserAsync(package.Owner.Name).ConfigureAwait(false);

                return new Package(
                    profile: new Profile(profile, _apiApplication),
                    package,
                    _apiApplication);
            }
            catch
            {
                return null;
            }
        }

        IEnumerable<PackageIdentity> packages = _installedPackageRepository.GetLocalPackages();
        var list = new List<LocalPackage>(packages.TryGetNonEnumeratedCount(out int count) ? count : 4);

        foreach (PackageIdentity packageId in packages)
        {
            string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
            if (Directory.Exists(directory))
            {
                Package? package = await GetPackage(packageId.Id).ConfigureAwait(false);
                if (package == null)
                {
                    var reader = new PackageFolderReader(directory);
                    list.Add(new LocalPackage(reader.NuspecReader)
                    {
                        InstalledPath = directory
                    });
                }
                else
                {
                    list.Add(new LocalPackage(package)
                    {
                        Version = packageId.Version.ToString(),
                        InstalledPath = directory,
                    });
                }
            }
        }

        return list;
    }

#pragma warning disable CA1822
    public IReadOnlyList<LocalPackage> GetSideLoadPackages()
#pragma warning restore CA1822
    {
        if (Directory.Exists(Helper.SideLoadsPath))
        {
            string[] items = Directory.GetDirectories(Helper.SideLoadsPath);
            var list = new List<LocalPackage>(items.Length);
            foreach (string item in items)
            {
                string name = Path.GetFileName(item);

                if (File.Exists(Path.Combine(item, $"{name}.dll")))
                {
                    list.Add(new LocalPackage
                    {
                        Name = name,
                        DisplayName = name,
                        InstalledPath = item,
                        SideLoad = true
                    });
                }
            }

            return list;
        }

        return Array.Empty<LocalPackage>();
    }

    public Assembly[] Load(LocalPackage package)
    {
        if (package.InstalledPath == null)
        {
            var packageId = new PackageIdentity(package.Name, NuGetVersion.Parse(package.Version));
            package.InstalledPath = Helper.PackagePathResolver.GetInstallPath(packageId);
        }

        Assembly[] assemblies = !package.SideLoad
            ? Load(package.InstalledPath)
            : SideLoad(package.InstalledPath);

        var extensions = new List<Extension>();

        foreach (Assembly assembly in assemblies)
        {
            LoadExtensions(assembly, extensions);
        }

        ExtensionProvider.AddExtensions(package.LocalId, extensions.ToArray());

        _loadedPackage.Add(package);

        return assemblies;
    }

    private static void LoadExtensions(Assembly assembly, List<Extension> extensions)
    {
        foreach (Type type in assembly.GetExportedTypes())
        {
            if (type.GetCustomAttribute<ExportAttribute>() is { })
            {
                if (type.IsAssignableTo(typeof(Extension))
                    && Activator.CreateInstance(type) is Extension extension)
                {
                    extension.Load();

                    extensions.Add(extension);
                }
            }
        }
    }

    private sealed class _Observable : LightweightObservableBase<bool>
    {
        private readonly PackageManager _manager;
        private readonly string _name;
        private readonly PackageIdentity? _packageIdentity;
        private IDisposable? _disposable;

        public _Observable(PackageManager manager, string name, string? version)
        {
            _manager = manager;
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
                observer.OnNext(_manager._loadedPackage.Any(
                    x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, _name)
                        && new NuGetVersion(x.Version) == _packageIdentity.Version));
            }
            else
            {
                observer.OnNext(_manager._loadedPackage.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, _name)));
            }
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;
        }

        protected override void Initialize()
        {
            _disposable = _manager._subject
                .Subscribe(OnReceived);
        }

        private void OnReceived((PackageIdentity Package, bool Loaded) obj)
        {
            if ((_packageIdentity != null && _packageIdentity == obj.Package)
                || StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                PublishNext(obj.Loaded);
            }
        }
    }
}
