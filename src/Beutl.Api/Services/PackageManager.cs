using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Beutl.Api.Objects;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Telemetry = Beutl.Api.Services.PackageManagemantActivitySource;

namespace Beutl.Api.Services;

public record LoadedPackageInfo(LocalPackage Package, PluginLoadContext? LoadContext);

public sealed partial class PackageManager : PackageLoader
{
    private readonly InstalledPackageRepository installedPackageRepository;
    private readonly IExtensionRegistry extensionRegistry;
    private readonly ContextCommandManager commandManager;
    private readonly BeutlApiApplication apiApplication;
    private readonly ILoadContextUnloadDiagnostics? unloadDiagnostics;
    private readonly ILogger _logger = Log.CreateLogger<PackageManager>();
    private readonly ConcurrentDictionary<int, LoadedPackageInfo> _loadedPackages = new();
    private readonly Dictionary<int, Task<bool>> _unloadOperations = [];
    private readonly Dictionary<int, PendingRollbackInfo> _pendingRollbacks = [];
    private readonly HashSet<int> _quarantinedPackages = [];
    private readonly HashSet<int> _loadingPackages = [];
    private readonly object _packageLifecycleGate = new();
    private readonly ExtensionSettingsStore _settingsStore = new();

    internal PackageManager(
        InstalledPackageRepository installedPackageRepository,
        IExtensionRegistry extensionRegistry,
        ContextCommandManager commandManager,
        BeutlApiApplication apiApplication,
        ILoadContextUnloadDiagnostics? unloadDiagnostics = null)
    {
        this.installedPackageRepository = installedPackageRepository;
        this.extensionRegistry = extensionRegistry;
        this.commandManager = commandManager;
        this.apiApplication = apiApplication;
        this.unloadDiagnostics = unloadDiagnostics;
    }

    public IEnumerable<LocalPackage> LoadedPackage => _loadedPackages.Values.Select(x => x.Package);

    internal IExtensionRegistry ExtensionRegistry => extensionRegistry;

    internal Action? AfterExtensionRegistration { get; set; }

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
            try
            {
                using FileStream stream = File.OpenRead(file);
                if (Helper.ReadLocalPackageFromNupkgFile(stream) is { } localPackage
                    && !packages.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Package.Name, localPackage.Name)))
                {
                    list.Add(localPackage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to read local package {Path}; continuing with other packages.", file);
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<PackageUpdate>> CheckUpdate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (discover, lifetime) = apiApplication.GetResourceWithLifetime<DiscoverService>(cancellationToken);
        using CancellationTokenSource operationCts = lifetime;
        CancellationToken operationToken = operationCts.Token;
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("CheckUpdate"))
        {
            PackageIdentity[] packages = installedPackageRepository.GetLocalPackages().ToArray();

            var updates = new List<PackageUpdate>(packages.Length);

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

                    PackageUpdate? update = await FindUpdateAsync(remotePackage, version, versionStr, operationToken)
                        .ConfigureAwait(false);
                    if (update is not null)
                    {
                        updates.Add(update);
                        _logger.LogInformation("Update found for package {PackageId}: {OldVersion} -> {NewVersion}", pkg.Id, versionStr, update.NewVersion.Version.Value);
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
        var (discover, lifetime) = apiApplication.GetResourceWithLifetime<DiscoverService>(cancellationToken);
        using CancellationTokenSource operationCts = lifetime;
        CancellationToken operationToken = operationCts.Token;
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("CheckUpdate"))
        {

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

                PackageUpdate? update = await FindUpdateAsync(remotePackage, version, pkg.Version, operationToken)
                    .ConfigureAwait(false);
                if (update is not null)
                {
                    _logger.LogInformation("Update found for package {PackageName}: {OldVersion} -> {NewVersion}", pkg.Name, versionStr, update.NewVersion.Version.Value);
                    return update;
                }
            }

            operationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static async Task<PackageUpdate?> FindUpdateAsync(
        Package remotePackage,
        NuGetVersion installedVersion,
        string installedVersionText,
        CancellationToken cancellationToken)
    {
        Release[] releases = await remotePackage
            .GetReleasesAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Release? item in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 降順
            if (new NuGetVersion(item.Version.Value).CompareTo(installedVersion) > 0)
            {
                Release? oldRelease = await TryGetReleaseAsync(
                        remotePackage,
                        installedVersionText,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new PackageUpdate(remotePackage, oldRelease, item);
            }
        }

        return null;
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

    private Action<string>? _dumpOpener;

    public LocalPackage[] FindLoadedPackage(string name)
    {
        return [.. _loadedPackages.Values
            .Select(x => x.Package)
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, name))];
    }
}
