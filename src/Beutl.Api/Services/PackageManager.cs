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
    IExtensionRegistry extensionRegistry,
    ContextCommandManager commandManager,
    BeutlApiApplication apiApplication,
    ILoadContextUnloadDiagnostics? unloadDiagnostics = null) : PackageLoader
{
    private readonly ILogger _logger = Log.CreateLogger<PackageManager>();
    private readonly ConcurrentDictionary<int, LoadedPackageInfo> _loadedPackages = new();
    private readonly Dictionary<int, Task<bool>> _unloadOperations = [];
    private readonly Dictionary<int, PendingRollbackInfo> _pendingRollbacks = [];
    private readonly HashSet<int> _quarantinedPackages = [];
    private readonly HashSet<int> _loadingPackages = [];
    private readonly object _packageLifecycleGate = new();
    private readonly ExtensionSettingsStore _settingsStore = new();

    public IEnumerable<LocalPackage> LoadedPackage => _loadedPackages.Values.Select(x => x.Package);

    public IExtensionRegistry ExtensionRegistry => extensionRegistry;

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

    public async Task<IReadOnlyList<PackageUpdate>> CheckUpdate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource operationCts = apiApplication.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken operationToken = operationCts.Token;
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("CheckUpdate"))
        {
            PackageIdentity[] packages = installedPackageRepository.GetLocalPackages().ToArray();

            var updates = new List<PackageUpdate>(packages.Length);
            DiscoverService discover = apiApplication.GetResource<DiscoverService>();

            for (int i = 0; i < packages.Length; i++)
            {
                operationToken.ThrowIfCancellationRequested();
                PackageIdentity pkg = packages[i];
                NuGetVersion version = pkg.Version;
                string versionStr = version.ToString();
                try
                {
                    activity?.AddEvent(new("Checking updates"));
                    activity?.SetTag("PackageId", pkg.Id);
                    activity?.SetTag("Version", versionStr);
                    Package remotePackage = await discover
                        .GetPackage(pkg.Id, operationToken)
                        .ConfigureAwait(false);
                    activity?.AddEvent(new("Checked updates"));

                    Release[] releases = await remotePackage
                        .GetReleasesAsync(operationToken)
                        .ConfigureAwait(false);

                    foreach (Release? item in releases)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        // 降順
                        if (new NuGetVersion(item.Version.Value).CompareTo(version) > 0)
                        {
                            Release? oldRelease = await TryGetReleaseAsync(
                                    remotePackage,
                                    versionStr,
                                    operationToken)
                                .ConfigureAwait(false);
                            updates.Add(new PackageUpdate(remotePackage, oldRelease, item));
                            _logger.LogInformation("Update found for package {PackageId}: {OldVersion} -> {NewVersion}", pkg.Id, versionStr, item.Version.Value);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "An exception occurred while checking for package updates. (PackageId: {PackageId})", pkg.Id);
                }
            }

            operationToken.ThrowIfCancellationRequested();
            return updates;
        }
    }

    public async Task<PackageUpdate?> CheckUpdate(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource operationCts = apiApplication.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken operationToken = operationCts.Token;
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
                Package remotePackage = await discover
                    .GetPackage(pkg.Name, operationToken)
                    .ConfigureAwait(false);
                activity?.AddEvent(new("Checked updates"));

                Release[] releases = await remotePackage
                    .GetReleasesAsync(operationToken)
                    .ConfigureAwait(false);

                foreach (Release? item in releases)
                {
                    operationToken.ThrowIfCancellationRequested();
                    // 降順
                    if (new NuGetVersion(item.Version.Value).CompareTo(version) > 0)
                    {
                        Release? oldRelease = await TryGetReleaseAsync(
                                remotePackage,
                                pkg.Version,
                                operationToken)
                            .ConfigureAwait(false);
                        _logger.LogInformation("Update found for package {PackageName}: {OldVersion} -> {NewVersion}", pkg.Name, versionStr, item.Version.Value);
                        return new PackageUpdate(remotePackage, oldRelease, item);
                    }
                }
            }

            operationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static async Task<Release?> TryGetReleaseAsync(
        Package package,
        string version,
        CancellationToken cancellationToken)
    {
        try
        {
            return await package.GetReleaseAsync(version, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
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
                string? directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
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

            // A material or template package ships no lib/ directory, so resolving a target
            // framework for it would throw. Its payload was already copied into the home
            // directory at install time and there is nothing to load here.
            if (package.Tags.GetPackageKind() != PackageKind.Extension)
            {
                activity?.SetTag("AssemblyCount", 0);
                return [];
            }

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

    public ValueTask<bool> Unload(LocalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        TaskCompletionSource<bool>? completion = null;
        Task<bool> operation;
        lock (_packageLifecycleGate)
        {
            if (_unloadOperations.TryGetValue(package.LocalId, out operation!))
                return new ValueTask<bool>(operation);

            if (_quarantinedPackages.Contains(package.LocalId))
                return new ValueTask<bool>(false);

            if (_loadingPackages.Contains(package.LocalId))
                return new ValueTask<bool>(false);

            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operation = completion.Task;
            _unloadOperations.Add(package.LocalId, operation);
        }

        _ = RunUnloadAsync(package, completion);
        return new ValueTask<bool>(operation);
    }

    private async Task RunUnloadAsync(
        LocalPackage package,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            completion.TrySetResult(await UnloadOnceAsync(package).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_packageLifecycleGate)
            {
                if (!_quarantinedPackages.Contains(package.LocalId))
                {
                    _unloadOperations.Remove(package.LocalId);
                }
            }
        }
    }

    private async Task<bool> UnloadOnceAsync(LocalPackage package)
    {
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("Unload"))
        {
            PackageUnloadResult? result = await UnloadCoreAsync(activity, package);
            if (result is null)
            {
                return false;
            }

            WeakReference weakReference = result.LoadContextReference;
            string[] assemblyNames = result.AssemblyNames;

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

    private async ValueTask<PackageUnloadResult?> UnloadCoreAsync(
        Activity? activity,
        LocalPackage package)
    {
        string[] assemblyNames = [];
        activity?.SetTag("PackageName", package.Name);

        if (package.LocalId == LocalPackage.Reserved0)
        {
            _logger.LogWarning("Cannot unload built-in extensions.");
            return null;
        }

        if (!_loadedPackages.TryGetValue(package.LocalId, out LoadedPackageInfo? info))
        {
            _logger.LogWarning("Package {PackageName} is not loaded.", package.Name);
            return null;
        }

        // Only the registries below have explicit operation leases. Other
        // extension families create long-lived editor/output/window objects,
        // so their package files are updated safely on the next restart.
        ExtensionRemoval? removal = null;
        var requiresRestart = false;
        List<Exception>? retirementFailures = null;
        try
        {
            extensionRegistry.SynchronizeMutation(() =>
            {
                IReadOnlyList<Extension> packageExtensions =
                    extensionRegistry.GetPackageExtensions(package.LocalId);
                if (packageExtensions.Any(extension => !SupportsLiveUnload(extension)))
                {
                    requiresRestart = true;
                    return;
                }

                removal = extensionRegistry.RemoveExtensions(package.LocalId);
            });
        }
        catch (ExtensionRemovalNotificationException ex)
        {
            removal = ex.Removal;
            retirementFailures = [ex];
        }
        if (requiresRestart)
        {
            _logger.LogInformation(
                "Package {PackageName} contains extensions that require restart for safe unload.",
                package.Name);
            return null;
        }

        if (removal is null)
        {
            throw new InvalidOperationException(
                $"The extension registry did not remove package {package.Name}.");
        }

        IReadOnlyList<Extension> extensions = removal.Extensions;
        foreach (Extension ext in extensions)
        {
            if (ext is ViewExtension viewExtension)
            {
                try
                {
                    // Commands are a discoverability surface too. Retire them before waiting so
                    // no new package-owned callback can start while the package is draining.
                    commandManager.Unregister(viewExtension);
                }
                catch (Exception ex)
                {
                    (retirementFailures ??= []).Add(ex);
                    _logger.LogError(
                        ex,
                        "Failed to unregister commands for extension {ExtensionName}.",
                        ext.GetType().Name);
                }
            }

            try
            {
                // Configuration notifications can call package code and therefore belong to the
                // synchronous retirement phase, not post-unload cleanup.
                CleanupExtensionSettings(ext);
            }
            catch (Exception ex)
            {
                (retirementFailures ??= []).Add(ex);
                _logger.LogError(
                    ex,
                    "Failed to detach settings for extension {ExtensionName}.",
                    ext.GetType().Name);
            }
        }

        try
        {
            // Drain every extension in the package before invoking any extension-level unload.
            // Packages commonly share static resources across several extension entry points.
            await removal.DrainAsync();
        }
        catch (Exception ex)
        {
            (retirementFailures ??= []).Add(ex);
            _logger.LogError(
                ex,
                "Package {PackageName} registrations failed to drain; the load context remains quarantined.",
                package.Name);
        }

        if (retirementFailures is not null)
        {
            lock (_packageLifecycleGate)
            {
                _quarantinedPackages.Add(package.LocalId);
            }

            activity?.SetStatus(ActivityStatusCode.Error, "Extension retirement failed");
            return null;
        }

        foreach (Extension ext in extensions)
        {
            try
            {
                ext.Unload();
                _logger.LogInformation("Extension {ExtensionName} unloaded.", ext.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload extension {ExtensionName}.", ext.GetType().Name);
            }

        }

        _loadedPackages.TryRemove(package.LocalId, out _);

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
        return new PackageUnloadResult(
            new WeakReference(info.LoadContext, trackResurrection: true),
            assemblyNames);
    }

    private sealed record PackageUnloadResult(
        WeakReference LoadContextReference,
        string[] AssemblyNames);

    private static bool SupportsLiveUnload(Extension extension)
        => extension is ILiveUnloadExtension;

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
        ExtensionRemoval? rollbackRemoval = null;
        var addedToProvider = false;
        var addedToLoadedPackages = false;
        bool alreadyKnown;
        lock (_packageLifecycleGate)
        {
            alreadyKnown = _loadingPackages.Contains(package.LocalId)
                           || _unloadOperations.ContainsKey(package.LocalId)
                           || _quarantinedPackages.Contains(package.LocalId)
                           || _loadedPackages.ContainsKey(package.LocalId);
            if (!alreadyKnown)
            {
                _loadingPackages.Add(package.LocalId);
            }
        }

        if (alreadyKnown)
        {
            // The caller resolved the package's assemblies into a collectible
            // context before this could be known. Nothing will ever reference
            // them, so the context goes with the rejection rather than staying
            // loaded for the life of the process.
            if (loadContext is { })
            {
                TryUnloadLoadContext(package, loadContext);
            }

            throw new InvalidOperationException(
                $"Package {package.Name} is already loaded, loading, unloading, or quarantined.");
        }

        try
        {
            extensions = LoadPackageExtensions(extensionTypes);

            activity?.AddEvent(new ActivityEvent("Extensions loaded"));
            activity?.SetTag("ExtensionCount", extensions.Count);

            ExtensionRegistry.AddExtensions(package.LocalId, extensions);
            addedToProvider = true;

            lock (_packageLifecycleGate)
            {
                if (!_loadedPackages.TryAdd(
                        package.LocalId,
                        new LoadedPackageInfo(package, loadContext)))
                {
                    throw new InvalidOperationException(
                        $"Package {package.Name} is already loaded, unloading, or quarantined.");
                }

                _loadingPackages.Remove(package.LocalId);
            }
            addedToLoadedPackages = true;

            return assemblies;
        }
        catch (Exception loadFailure)
        {
            if (loadFailure is ExtensionRegistrationNotificationException registrationFailure)
            {
                rollbackRemoval = registrationFailure.Removal;
            }
            if (addedToProvider)
            {
                try
                {
                    rollbackRemoval = ExtensionRegistry.RemoveExtensions(package.LocalId);
                }
                catch (ExtensionRemovalNotificationException ex)
                {
                    rollbackRemoval = ex.Removal;
                }
            }
            if (addedToLoadedPackages)
            {
                lock (_packageLifecycleGate)
                {
                    _loadedPackages.TryRemove(package.LocalId, out _);
                }
            }

            bool rollbackPending = rollbackRemoval is not null;
            lock (_packageLifecycleGate)
            {
                _loadingPackages.Remove(package.LocalId);
                if (rollbackPending)
                {
                    _quarantinedPackages.Add(package.LocalId);
                }
            }

            if (rollbackRemoval is null)
            {
                // LoadPackageExtensions already rolls back on failure, so extensions is non-empty
                // only when a later registration step threw; this is not a double-unload.
                RollbackLoadedExtensions(extensions);
                if (loadContext is { })
                {
                    TryUnloadLoadContext(package, loadContext);
                }
            }
            else
            {
                StartRollbackAfterDrain(
                    package,
                    extensions,
                    loadContext,
                    rollbackRemoval);
            }

            throw;
        }
    }

    private void StartRollbackAfterDrain(
        LocalPackage package,
        List<Extension> extensions,
        PluginLoadContext? loadContext,
        ExtensionRemoval removal)
    {
        Extension[] rollbackExtensions = extensions.ToArray();
        extensions.Clear();
        var pendingRollback = new PendingRollbackInfo(
            package,
            rollbackExtensions,
            loadContext);
        lock (_packageLifecycleGate)
        {
            _pendingRollbacks.Add(package.LocalId, pendingRollback);
        }
        pendingRollback.Operation = DrainAndRollbackAsync(
            pendingRollback,
            removal);
    }

    private async Task DrainAndRollbackAsync(
        PendingRollbackInfo pendingRollback,
        ExtensionRemoval removal)
    {
        try
        {
            await removal.DrainAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_packageLifecycleGate)
            {
                _quarantinedPackages.Add(pendingRollback.Package.LocalId);
            }
            _logger.LogError(
                ex,
                "Package {PackageName} failed to drain after registration rollback; its load context remains quarantined.",
                pendingRollback.Package.Name);
            return;
        }

        var rollbackList = pendingRollback.Extensions.ToList();
        RollbackLoadedExtensions(rollbackList);
        if (pendingRollback.LoadContext is not null)
        {
            TryUnloadLoadContext(pendingRollback.Package, pendingRollback.LoadContext);
        }
        lock (_packageLifecycleGate)
        {
            _pendingRollbacks.Remove(pendingRollback.Package.LocalId);
            _quarantinedPackages.Remove(pendingRollback.Package.LocalId);
        }
    }

    private sealed class PendingRollbackInfo(
        LocalPackage package,
        IReadOnlyList<Extension> extensions,
        PluginLoadContext? loadContext)
    {
        public LocalPackage Package { get; } = package;

        public IReadOnlyList<Extension> Extensions { get; } = extensions;

        public PluginLoadContext? LoadContext { get; } = loadContext;

        public Task? Operation { get; set; }
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
