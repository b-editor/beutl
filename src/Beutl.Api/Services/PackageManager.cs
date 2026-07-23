using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using Beutl.Api.Objects;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Services;
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
    BeutlApiApplication apiApplication,
    ILoadContextUnloadDiagnostics? unloadDiagnostics = null) : PackageLoader
{
    private readonly ILogger _logger = Log.CreateLogger<PackageManager>();
    private readonly ConcurrentDictionary<int, LoadedPackageInfo> _loadedPackages = new();
    private readonly ExtensionSettingsStore _settingsStore = new();

    public IEnumerable<LocalPackage> LoadedPackage => _loadedPackages.Values.Select(x => x.Package);

    public ExtensionProvider ExtensionProvider => extensionProvider;

    public ContextCommandManager ContextCommandManager => commandManager;

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

            // Strict on purpose: GetExportedTypes throws on an unresolvable type so a broken plugin
            // fails the load and rolls back instead of registering with extensions silently skipped.
            // Unload stays lenient (GetLoadableTypes) since cleanup must proceed regardless.
            return LoadExtensionsAndRegister(
                activity,
                package,
                result.Assemblies,
                result.LoadContext,
                result.Assemblies.SelectMany(assembly => assembly.GetExportedTypes()));
        }
    }

    public async ValueTask<bool> Unload(LocalPackage package)
    {
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("Unload"))
        {
            var result = UnloadCore(activity, package, out WeakReference? weakReference, out string[] assemblyNames);
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

            bool unloaded = !weakReference.IsAlive;
            if (!unloaded && unloadDiagnostics is { } diagnostics && assemblyNames.Length > 0)
            {
                activity?.AddEvent(new ActivityEvent("Prompting for unload diagnostics"));
                // Ask before snapshotting: the ClrMD self-snapshot is heavy, so the developer decides whether it runs.
                PromptCaptureUnloadDiagnostics(diagnostics, package.Name, assemblyNames);
            }

            return unloaded;
        }
    }

    private bool UnloadCore(
        Activity? activity, LocalPackage package,
        [NotNullWhen(true)] out WeakReference? weakReference, out string[] assemblyNames)
    {
        weakReference = null;
        assemblyNames = [];
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
                // Capture only the assembly names as strings; never retain the assemblies/types, or the diagnostics
                // pass below would itself root the context it is meant to diagnose.
                assemblyNames = [.. loadContext.Assemblies
                    .Select(a => a.GetName().Name)
                    .OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception ex)
            {
                // Best-effort like TryUnloadLoadContext: a reflection failure here must not break the unload flow.
                _logger.LogWarning(ex, "Failed to capture assembly names for unload diagnostics of {PackageName}.", package.Name);
            }

            TryUnloadLoadContext(package, loadContext);
        }

        // https://learn.microsoft.com/ja-jp/dotnet/standard/assembly/unloadability#use-a-custom-collectible-assemblyloadcontext
        weakReference = new WeakReference(info.LoadContext, trackResurrection: true);
        return true;
    }

    private Action<string>? _dumpOpener;

    // Test seam: a unit test substitutes this to assert the capture opens the written dump path without launching a
    // real process. Production leaves it as OpenDumpFile.
    internal Action<string> DumpOpener
    {
        get => _dumpOpener ??= OpenDumpFile;
        set => _dumpOpener = value;
    }

    // Diagnostics are wired only in Debug builds (BeutlApiApplication injects null in Release), so [Conditional]
    // strips this prompt the same way, keeping the offer in step with the capture it would trigger.
    [Conditional("DEBUG")]
    internal void PromptCaptureUnloadDiagnostics(
        ILoadContextUnloadDiagnostics diagnostics, string packageName, string[] assemblyNames)
    {
        NotificationService.ShowWarning(
            $"Failed to unload '{packageName}'",
            "The extension's load context is still alive. Capture a diagnostics dump to find what is keeping the "
            + "assemblies loaded?",
            expiration: TimeSpan.FromSeconds(30),
            actions:
            [
                // Offload: the ClrMD self-snapshot is heavy and must not block the UI thread the click runs on.
                new NotificationAction(
                    "Capture dump",
                    () => { _ = Task.Run(() => CaptureAndOpenUnloadDump(diagnostics, packageName, assemblyNames)); })
            ]);
    }

    // Synchronous so a test can drive it directly; production reaches it from the prompt action's Task.Run. Contained
    // because CaptureUnloadFailure is a public interface a third-party implementation could throw from.
    internal void CaptureAndOpenUnloadDump(
        ILoadContextUnloadDiagnostics diagnostics, string packageName, string[] assemblyNames)
    {
        try
        {
            string? dumpPath = diagnostics.CaptureUnloadFailure(packageName, assemblyNames);
            if (!string.IsNullOrEmpty(dumpPath))
            {
                DumpOpener(dumpPath);
            }
            else
            {
                // The capture ran but produced nothing (context already collected / another capture holds the gate /
                // snapshotting unsupported). The click already dismissed the prompt, so acknowledge it or it looks dead.
                NotifyDiagnosticsUnavailable(packageName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unload diagnostics capture threw for {PackageName}.", packageName);
        }
    }

    private static void NotifyDiagnosticsUnavailable(string packageName)
    {
        NotificationService.ShowInformation(
            $"No dump captured for '{packageName}'",
            "The load context was already collected, another capture is in progress, or snapshotting is "
            + "unavailable. See the log for details.");
    }

    private void OpenDumpFile(string dumpPath)
    {
        try
        {
            // UseShellExecute routes to the OS handler (ShellExecute / open / xdg-open) to open the .txt on any platform.
            Process.Start(new ProcessStartInfo(dumpPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open unload diagnostics dump at {DumpPath}.", dumpPath);
        }
    }

    public LocalPackage[] FindLoadedPackage(string name)
    {
        return [.. _loadedPackages.Values
            .Select(x => x.Package)
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, name))];
    }

    internal Assembly[] LoadExtensionsAndRegister(
        Activity? activity,
        LocalPackage package,
        Assembly[] assemblies,
        PluginLoadContext? loadContext,
        IEnumerable<Type> extensionTypes)
    {
        List<Extension> extensions = [];
        var addedToProvider = false;
        try
        {
            extensions = LoadPackageExtensions(extensionTypes);

            activity?.AddEvent(new ActivityEvent("Extensions loaded"));
            activity?.SetTag("ExtensionCount", extensions.Count);

            ExtensionProvider.AddExtensions(package.LocalId, extensions.ToArray());
            addedToProvider = true;

            if (!_loadedPackages.TryAdd(package.LocalId, new LoadedPackageInfo(package, loadContext)))
            {
                throw new InvalidOperationException($"Package {package.Name} is already loaded.");
            }

            return assemblies;
        }
        catch
        {
            if (addedToProvider)
            {
                ExtensionProvider.RemoveExtensions(package.LocalId);
            }

            // LoadPackageExtensions already rolls back on failure, so extensions is non-empty
            // only when a later registration step threw; this is not a double-unload.
            RollbackLoadedExtensions(extensions);
            if (loadContext is { })
            {
                TryUnloadLoadContext(package, loadContext);
            }

            throw;
        }
    }

    internal List<Extension> LoadPackageExtensions(IEnumerable<Type> extensionTypes)
    {
        var extensions = new List<Extension>();
        try
        {
            foreach (Type type in extensionTypes)
            {
                LoadExtension(type, extensions);
            }

            return extensions;
        }
        catch
        {
            RollbackLoadedExtensions(extensions);
            throw;
        }
    }

    private void LoadExtension(Type type, List<Extension> extensions)
    {
        if (type.GetCustomAttribute<ExportAttribute>() is { }
            && type.IsAssignableTo(typeof(Extension))
            && Activator.CreateInstance(type) is Extension extension)
        {
            var loadStarted = false;
            try
            {
                SetupExtensionSettings(extension);
                if (extension is ViewExtension viewExtension)
                {
                    commandManager.Register(viewExtension);
                }

                loadStarted = true;
                extension.Load();

                extensions.Add(extension);
                _logger.LogInformation("Extension {ExtensionName} loaded from assembly {AssemblyName}", type.Name, type.Assembly.GetName().Name);
            }
            catch
            {
                RollbackExtensionLoad(extension, loadStarted);
                throw;
            }
        }
    }

    private void RollbackLoadedExtensions(List<Extension> extensions)
    {
        for (int i = extensions.Count - 1; i >= 0; i--)
        {
            RollbackExtensionLoad(extensions[i], unload: true);
        }

        extensions.Clear();
    }

    private void RollbackExtensionLoad(Extension extension, bool unload)
    {
        if (extension is ViewExtension viewExtension)
        {
            try
            {
                commandManager.Unregister(viewExtension);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unregister commands while rolling back extension {ExtensionName}.", extension.GetType().Name);
            }
        }

        if (unload)
        {
            try
            {
                extension.Unload();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload extension {ExtensionName} while rolling back load.", extension.GetType().Name);
            }
        }

        try
        {
            CleanupExtensionSettings(extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up settings while rolling back extension {ExtensionName}.", extension.GetType().Name);
        }
    }

    private void TryUnloadLoadContext(LocalPackage package, PluginLoadContext loadContext)
    {
        try
        {
            Type[] types = loadContext.Assemblies.SelectMany(GetLoadableTypes).ToArray();
            TypeUnloadNotifier.NotifyUnloading(types);
            AvaloniaPropertyRegistry.Instance.UnregisterByModule(types);
            foreach (string name in loadContext.Assemblies.Select(a => a.GetName().Name).OfType<string>())
            {
                AssetLoader.InvalidateAssemblyCache(name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up type registrations for {PackageName}.", package.Name);
        }

        try
        {
            loadContext.Unload();
            _logger.LogInformation("AssemblyLoadContext unloaded for {PackageName}.", package.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload AssemblyLoadContext for {PackageName}.", package.Name);
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
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
}
