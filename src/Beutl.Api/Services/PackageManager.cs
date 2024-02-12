using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Reflection;

using Beutl.Api.Objects;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Reactive;

using Microsoft.Extensions.Logging;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

using Telemetry = Beutl.Api.Services.PackageManagemantActivitySource;

namespace Beutl.Api.Services;

public sealed class PackageManager(
    InstalledPackageRepository installedPackageRepository,
    ExtensionProvider extensionProvider,
    BeutlApiApplication apiApplication) : PackageLoader
{
    private readonly ILogger _logger = Log.CreateLogger<PackageManager>();
    private readonly ConcurrentBag<LocalPackage> _loadedPackage = [];
    private readonly ExtensionSettingsStore _settingsStore = new();
    private readonly Subject<(PackageIdentity Package, bool Loaded)> _subject = new();

    public IEnumerable<LocalPackage> LoadedPackage => _loadedPackage;

    public ExtensionProvider ExtensionProvider { get; } = extensionProvider;

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
        if (!Directory.Exists(Helper.LocalSourcePath))
        {
            return [];
        }

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
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("CheckUpdate"))
        {
            PackageIdentity[] packages = installedPackageRepository.GetLocalPackages().ToArray();

            var updates = new List<PackageUpdate>(packages.Length);
            DiscoverService discover = apiApplication.GetResource<DiscoverService>();

            for (int i = 0; i < packages.Length; i++)
            {
                PackageIdentity pkg = packages[i];
                NuGetVersion version = pkg.Version;
                string versionStr = version.ToString();
                try
                {
                    activity?.AddEvent(new("Start_GetPackage"));
                    Package remotePackage = await discover.GetPackage(pkg.Id).ConfigureAwait(false);
                    activity?.AddEvent(new("Done_GetPackage"));

                    Release[] releases = await remotePackage.GetReleasesAsync().ConfigureAwait(false);

                    foreach (Release? item in releases)
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
                catch(Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred while checking for package updates. (PackageId: {PackageId})", pkg.Id);
                }
            }

            return updates;
        }
    }

    public async Task<PackageUpdate?> CheckUpdate(string name)
    {
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("CheckUpdate"))
        {
            DiscoverService discover = apiApplication.GetResource<DiscoverService>();

            LocalPackage? pkg = _loadedPackage.FirstOrDefault(v => !v.SideLoad && StringComparer.OrdinalIgnoreCase.Equals(v.Name, name));
            if (pkg != null)
            {
                string versionStr = pkg.Version;
                var version = new NuGetVersion(versionStr);
                activity?.AddEvent(new("Start_GetPackage"));
                Package remotePackage = await discover.GetPackage(pkg.Name).ConfigureAwait(false);
                activity?.AddEvent(new("Done_GetPackage"));

                Release[] releases = await remotePackage.GetReleasesAsync().ConfigureAwait(false);

                foreach (Release? item in releases)
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
    }

    public Task<IReadOnlyList<LocalPackage>> GetPackages()
    {
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("GetPackages"))
        {
            PackageIdentity[] packages = installedPackageRepository.GetLocalPackages().ToArray();
            activity?.SetTag("Packages_Count", packages.Length);

            var list = new List<LocalPackage>(packages.Length);

            foreach (PackageIdentity packageId in packages)
            {
                string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
                if (Directory.Exists(directory))
                {
                    var reader = new PackageFolderReader(directory);
                    list.Add(new LocalPackage(reader.NuspecReader)
                    {
                        InstalledPath = directory
                    });
                }
            }

            return Task.FromResult<IReadOnlyList<LocalPackage>>(list);
        }
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
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("Load"))
        {
            if (package.InstalledPath == null)
            {
                var packageId = new PackageIdentity(package.Name, NuGetVersion.Parse(package.Version));
                package.InstalledPath = Helper.PackagePathResolver.GetInstallPath(packageId);
            }

            Assembly[] assemblies = !package.SideLoad
                ? Load(package.InstalledPath)
                : SideLoad(package.InstalledPath);

            activity?.AddEvent(new ActivityEvent("Loaded_Assemblies"));
            activity?.SetTag("Assembly_Count", assemblies.Length);

            var extensions = new List<Extension>();

            foreach (Assembly assembly in assemblies)
            {
                LoadExtensions(assembly, extensions);
            }

            activity?.AddEvent(new ActivityEvent("Loaded_Extensions"));

            ExtensionProvider.AddExtensions(package.LocalId, [.. extensions]);

            _loadedPackage.Add(package);

            return assemblies;
        }
    }

    private void LoadExtensions(Assembly assembly, List<Extension> extensions)
    {
        foreach (Type type in assembly.GetExportedTypes())
        {
            if (type.GetCustomAttribute<ExportAttribute>() is { })
            {
                if (type.IsAssignableTo(typeof(Extension))
                    && Activator.CreateInstance(type) is Extension extension)
                {
                    SetupExtensionSettings(extension);
                    extension.Load();

                    extensions.Add(extension);
                }
            }
        }
    }

    internal void SetupExtensionSettings(Extension extension)
    {
        if (extension.Settings is { } settings)
        {
            _settingsStore.Restore(extension, settings);

            settings.ConfigurationChanged += (_, _) => _settingsStore.Save(extension, settings);
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
