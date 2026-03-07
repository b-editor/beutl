using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;
using System.Reflection;
using Avalonia;
using Beutl.Api.Objects;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.NodeTree;
using Beutl.Reactive;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Telemetry = Beutl.Api.Services.PackageManagemantActivitySource;

namespace Beutl.Api.Services;

public record LoadedPackageInfo(LocalPackage Package, PluginLoadContext? LoadContext);

public sealed class PackageManager(
    InstalledPackageRepository installedPackageRepository,
    ExtensionProvider extensionProvider,
    ContextCommandManager commandManager,
    BeutlApiApplication apiApplication) : PackageLoader
{
    private readonly ILogger _logger = Log.CreateLogger<PackageManager>();
    private readonly ConcurrentDictionary<int, LoadedPackageInfo> _loadedPackages = new();
    private readonly ExtensionSettingsStore _settingsStore = new();
    private readonly Subject<(PackageIdentity Package, bool Loaded)> _subject = new();

    public IEnumerable<LocalPackage> LoadedPackage => _loadedPackages.Values.Select(x => x.Package);

    public ExtensionProvider ExtensionProvider => extensionProvider;

    public ContextCommandManager ContextCommandManager => commandManager;

    public IObservable<bool> GetObservable(string name, string? version = null)
    {
        return new _Observable(this, name, version);
    }

    public bool IsLoaded(string name, string? version = null)
    {
        var packages = _loadedPackages.Values;
        if (version is { })
        {
            var nugetVersion = new NuGetVersion(version);
            return packages.Any(
                x => StringComparer.OrdinalIgnoreCase.Equals(x.Package.Name, name)
                     && new NuGetVersion(x.Package.Version) == nugetVersion);
        }
        else
        {
            return packages.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Package.Name, name));
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
        var packages = _loadedPackages.Values;

        foreach (string file in files)
        {
            using FileStream stream = File.OpenRead(file);
            if (Helper.ReadLocalPackageFromNupkgFile(stream) is { } localPackage)
            {
                if (!packages.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Package.Name, localPackage.Name)))
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
                    activity?.AddEvent(new("Checking updates"));
                    activity?.SetTag("PackageId", pkg.Id);
                    activity?.SetTag("Version", versionStr);
                    Package remotePackage = await discover.GetPackage(pkg.Id).ConfigureAwait(false);
                    activity?.AddEvent(new("Checked updates"));

                    Release[] releases = await remotePackage.GetReleasesAsync().ConfigureAwait(false);

                    foreach (Release? item in releases)
                    {
                        // 降順
                        if (new NuGetVersion(item.Version.Value).CompareTo(version) > 0)
                        {
                            Release? oldRelease = await Helper
                                .TryGetOrDefault(() => remotePackage.GetReleaseAsync(versionStr))
                                .ConfigureAwait(false);
                            updates.Add(new PackageUpdate(remotePackage, oldRelease, item));
                            _logger.LogInformation("Update found for package {PackageId}: {OldVersion} -> {NewVersion}", pkg.Id, versionStr, item.Version.Value);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "An exception occurred while checking for package updates. (PackageId: {PackageId})", pkg.Id);
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

            LocalPackage? pkg = _loadedPackages.Values
                .Select(x => x.Package)
                .FirstOrDefault(v =>
                    !v.SideLoad && StringComparer.OrdinalIgnoreCase.Equals(v.Name, name));
            if (pkg != null)
            {
                string versionStr = pkg.Version;
                var version = new NuGetVersion(versionStr);
                activity?.AddEvent(new("Checking updates"));
                activity?.SetTag("PackageName", pkg.Name);
                activity?.SetTag("Version", versionStr);
                Package remotePackage = await discover.GetPackage(pkg.Name).ConfigureAwait(false);
                activity?.AddEvent(new("Checked updates"));

                Release[] releases = await remotePackage.GetReleasesAsync().ConfigureAwait(false);

                foreach (Release? item in releases)
                {
                    // 降順
                    if (new NuGetVersion(item.Version.Value).CompareTo(version) > 0)
                    {
                        Release? oldRelease = await Helper
                            .TryGetOrDefault(() => remotePackage.GetReleaseAsync(pkg.Version))
                            .ConfigureAwait(false);
                        _logger.LogInformation("Update found for package {PackageName}: {OldVersion} -> {NewVersion}", pkg.Name, versionStr, item.Version.Value);
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
            activity?.SetTag("PackagesCount", packages.Length);

            var list = new List<LocalPackage>(packages.Length);

            foreach (PackageIdentity packageId in packages)
            {
                string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
                if (Directory.Exists(directory))
                {
                    var reader = new PackageFolderReader(directory);
                    list.Add(new LocalPackage(reader.NuspecReader) { InstalledPath = directory });
                }
            }

            return Task.FromResult<IReadOnlyList<LocalPackage>>(list);
        }
    }

    public IReadOnlyList<LocalPackage> GetSideLoadPackages()
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
                    _logger.LogInformation("Side-loaded package found: {PackageName}", name);
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
            activity?.SetTag("PackageName", package.Name);
            activity?.SetTag("PackageVersion", package.Version);
            if (package.InstalledPath == null)
            {
                var packageId = new PackageIdentity(package.Name, NuGetVersion.Parse(package.Version));
                package.InstalledPath = Helper.PackagePathResolver.GetInstallPath(packageId);
            }

            PackageLoadResult result = !package.SideLoad
                ? Load(package.InstalledPath)
                : SideLoad(package.InstalledPath);

            activity?.AddEvent(new ActivityEvent("Assemblies loaded"));
            activity?.SetTag("AssemblyCount", result.Assemblies.Length);

            var extensions = new List<Extension>();

            foreach (Assembly assembly in result.Assemblies)
            {
                LoadExtensions(assembly, extensions);
            }

            activity?.AddEvent(new ActivityEvent("Extensions loaded"));
            activity?.SetTag("ExtensionCount", extensions.Count);

            ExtensionProvider.AddExtensions(package.LocalId, extensions.ToArray());

            _loadedPackages.TryAdd(package.LocalId, new LoadedPackageInfo(package, result.LoadContext));

            var pkgId = new PackageIdentity(package.Name,
                string.IsNullOrEmpty(package.Version) ? null : NuGetVersion.Parse(package.Version));
            _subject.OnNext((pkgId, true));

            return result.Assemblies;
        }
    }

    public async ValueTask<bool> Unload(LocalPackage package)
    {
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("Unload"))
        {
            var result = UnloadCore(activity, package, out WeakReference? weakReference);
            if (!result || weakReference == null)
            {
                return false;
            }

            for (int i = 0; weakReference.IsAlive && (i < 10); i++)
            {
                GC.Collect();
                GC.WaitForFullGCComplete(-1);
                GC.WaitForPendingFinalizers();
                await Task.Delay(100).ConfigureAwait(false);
            }

            return !weakReference.IsAlive;
        }
    }

    private bool UnloadCore(Activity? activity, LocalPackage package, [NotNullWhen(true)] out WeakReference? weakReference)
    {
        weakReference = null;
        activity?.SetTag("PackageName", package.Name);

        if (package.LocalId == LocalPackage.Reserved0)
        {
            _logger.LogWarning("Cannot unload built-in extensions.");
            return false;
        }

        if (!_loadedPackages.TryRemove(package.LocalId, out LoadedPackageInfo? info))
        {
            _logger.LogWarning("Package {PackageName} is not loaded.", package.Name);
            return false;
        }

        Extension[] extensions = extensionProvider.RemoveExtensions(package.LocalId);
        foreach (Extension ext in extensions)
        {
            try
            {
                if (ext is ViewExtension viewExtension)
                {
                    commandManager.Unregister(viewExtension);
                }

                ext.Unload();
                CleanupExtensionSettings(ext);

                _logger.LogInformation("Extension {ExtensionName} unloaded.", ext.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload extension {ExtensionName}.", ext.GetType().Name);
            }
        }

        if (info.LoadContext is { } loadContext)
        {
            try
            {
                var types = loadContext.Assemblies.SelectMany(a => a.GetTypes()).ToArray();
                Beutl.Services.LibraryService.Current.Unregister(types);
                PropertyRegistry.Unregister(types);
                NodeRegistry.Unregister(types);
                TypeDisplayHelpers.Unregister(types);
                EngineObject.ReflectionCache.Unregister(types);
                ArrayTypeHelpers.Unregister(types);
                DefaultValueHelpers.Unregister(types);
                var r = AvaloniaPropertyRegistry.Instance.UnregisterByModule(types);
                loadContext.Unload();
                _logger.LogInformation("AssemblyLoadContext unloaded for {PackageName}.", package.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload AssemblyLoadContext for {PackageName}.", package.Name);
            }
        }

        var pkgId = new PackageIdentity(package.Name,
            string.IsNullOrEmpty(package.Version) ? null : NuGetVersion.Parse(package.Version));
        _subject.OnNext((pkgId, false));

        // https://learn.microsoft.com/ja-jp/dotnet/standard/assembly/unloadability#use-a-custom-collectible-assemblyloadcontext
        weakReference = new WeakReference(info.LoadContext, trackResurrection: true);
        return true;
    }

    public LocalPackage[] FindLoadedPackage(string name)
    {
        return [.. _loadedPackages.Values
            .Select(x => x.Package)
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, name))];
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
                    if (extension is ViewExtension viewExtension)
                    {
                        commandManager.Register(viewExtension);
                    }
                    extension.Load();

                    extensions.Add(extension);
                    _logger.LogInformation("Extension {ExtensionName} loaded from assembly {AssemblyName}", type.Name, assembly.GetName().Name);
                }
            }
        }
    }

    internal void SetupExtensionSettings(Extension extension)
    {
        if (extension.Settings is { } settings)
        {
            _settingsStore.Restore(extension, settings);

            EventHandler handler = (_, _) => _settingsStore.Save(extension, settings);
            extension.SettingsChangedHandler = handler;
            settings.ConfigurationChanged += handler;
            _logger.LogInformation("Settings restored for extension {ExtensionName}", extension.GetType().Name);
        }
    }

    private void CleanupExtensionSettings(Extension extension)
    {
        if (extension.Settings is { } settings && extension.SettingsChangedHandler is { } handler)
        {
            settings.ConfigurationChanged -= handler;
            extension.SettingsChangedHandler = null;
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
            var packages = _manager._loadedPackages.Values;
            if (_packageIdentity is { })
            {
                observer.OnNext(packages.Any(
                    x => StringComparer.OrdinalIgnoreCase.Equals(x.Package.Name, _name)
                         && new NuGetVersion(x.Package.Version) == _packageIdentity.Version));
            }
            else
            {
                observer.OnNext(
                    packages.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Package.Name, _name)));
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
