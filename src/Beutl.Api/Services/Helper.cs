using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;

using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

using static Beutl.Api.Services.CoreLibraries;

namespace Beutl.Api.Services;

internal static class Helper
{
    public static readonly string AppRoot;
    public static readonly string LocalSourcePath;
    public static readonly string InstallPath;
    public static readonly string SideLoadsPath;
    public static readonly PackagePathResolver PackagePathResolver;
    public static readonly FrameworkReducer FrameworkReducer;

    static Helper()
    {
        AppRoot = BeutlEnvironment.GetHomeDirectoryPath();
        LocalSourcePath = Path.Combine(AppRoot, "packageSource");
        InstallPath = Path.Combine(AppRoot, "packages");
        SideLoadsPath = Path.Combine(AppRoot, "sideloads");

        PackagePathResolver = new PackagePathResolver(InstallPath);

        FrameworkReducer = new FrameworkReducer();
    }

    public static NuGetFramework GetFrameworkName()
    {
        TargetFrameworkAttribute fx = Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()!;
        TargetPlatformAttribute? platform = Assembly.GetExecutingAssembly().GetCustomAttribute<TargetPlatformAttribute>();
        var frameworkName = new FrameworkName(fx.FrameworkName);

        if (platform != null)
        {
            return NuGetFramework.ParseComponents(frameworkName.FullName, platform.PlatformName);
        }

        return NuGetFramework.Parse(frameworkName.FullName);
    }

    // With preferInstalledVersions, a dependency that an installed version satisfies is resolved from the
    // installed versions instead of from its minimum version, so it needs no other source.
    public static async Task GetPackageDependencies(PackageIdentity package,
        NuGetFramework framework,
        SourceCacheContext cacheContext,
        ILogger logger,
        IEnumerable<SourceRepository> repositories,
        ISet<SourcePackageDependencyInfo> availablePackages,
        CancellationToken cancellationToken = default,
        bool preferInstalledVersions = false)
    {
        if (availablePackages.Contains(package) || IncludedInPackageDependencies(package.Id, package.Version)) return;

        foreach (SourceRepository sourceRepository in repositories)
        {
            DependencyInfoResource? dependencyInfoResource
                = await sourceRepository.GetResourceAsync<DependencyInfoResource>(cancellationToken)
                    .ConfigureAwait(false);

            if (dependencyInfoResource == null) continue;

            SourcePackageDependencyInfo dependencyInfo
                = await dependencyInfoResource.ResolvePackage(
                    package, framework, cacheContext, logger, cancellationToken)
                        .ConfigureAwait(false);

            if (dependencyInfo == null) continue;

            if (dependencyInfo.Dependencies.Any(x => IncludedInPackageDependencies(x.Id, x.VersionRange)))
            {
                dependencyInfo = new SourcePackageDependencyInfo(
                    dependencyInfo,
                    dependencyInfo.Dependencies.Where(x => !IncludedInPackageDependencies(x.Id, x.VersionRange)),
                    dependencyInfo.Listed,
                    dependencyInfo.Source,
                    dependencyInfo.DownloadUri,
                    dependencyInfo.PackageHash);
            }

            availablePackages.Add(dependencyInfo);
            foreach (PackageDependency? dependency in dependencyInfo.Dependencies)
            {
                IReadOnlyList<PackageIdentity> candidates = preferInstalledVersions ? GetInstalledVersions(dependency) : [];
                if (candidates.Count == 0)
                    candidates = [new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion)];

                foreach (PackageIdentity candidate in candidates)
                {
                    await GetPackageDependencies(
                        candidate,
                        framework,
                        cacheContext,
                        logger,
                        repositories,
                        availablePackages,
                        cancellationToken,
                        preferInstalledVersions)
                        .ConfigureAwait(false);
                }
            }

            // The first source that has the package is the one used. Asking the remaining sources
            // only adds requests, which fail when a remote source cannot be reached.
            return;
        }
    }

    // The extracted versions of a dependency that satisfy its version range.
    public static IReadOnlyList<PackageIdentity> GetInstalledVersions(PackageDependency dependency)
    {
        if (!Directory.Exists(InstallPath))
            return [];

        string prefix = dependency.Id + ".";
        var versions = new List<PackageIdentity>();
        foreach (string directory in Directory.GetDirectories(InstallPath))
        {
            string name = Path.GetFileName(directory);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && NuGetVersion.TryParse(name[prefix.Length..], out NuGetVersion? version)
                && dependency.VersionRange.Satisfies(version))
            {
                var identity = new PackageIdentity(dependency.Id, version);
                if (PackagePathResolver.GetInstalledPath(identity) != null)
                    versions.Add(identity);
            }
        }

        return versions;
    }

    public static string GetNupkgFilePath(string packageId, string version)
    {
        return Path.Combine(LocalSourcePath, $"{packageId}.{version}.nupkg");
    }

    // GetInstalledPath keys off the package's .nupkg file, so a directory whose .nupkg was deleted
    // resolves to null even though its extracted files remain. Fall back to the deterministic install
    // path so cleanup still reaches them; callers use Directory.Exists to tell gone from still-there.
    public static string ResolveInstalledDirectory(PackageIdentity package)
    {
        return PackagePathResolver.GetInstalledPath(package)
               ?? PackagePathResolver.GetInstallPath(package);
    }

    public static LocalPackage? ReadLocalPackageFromNupkgFile(Stream stream)
    {
        using var zip = new ZipArchive(stream);

        ZipArchiveEntry? nuspecEntry = zip.Entries.FirstOrDefault(x => x.Name.EndsWith(".nuspec") && !x.FullName.Contains('/'));
        if (nuspecEntry is { })
        {
            using (Stream nuspecStream = nuspecEntry.Open())
            {
                var nuspecReader = new NuspecReader(nuspecStream);

                return new LocalPackage(nuspecReader);
            }
        }

        return null;
    }
}
