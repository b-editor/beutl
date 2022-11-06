using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis.CSharp.Syntax;

using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;

namespace Beutl.Api.Services;

internal static class Helper
{
    public static readonly string AppRoot;
    public static readonly string LocalSourcePath;
    public static readonly string InstallPath;
    public static readonly PackagePathResolver PackagePathResolver;
    public static readonly FrameworkReducer FrameworkReducer;

    static Helper()
    {
        AppRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl");
        LocalSourcePath = Path.Combine(AppRoot, "packageSource");
        InstallPath = Path.Combine(AppRoot, "packages");

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

    public static async Task GetPackageDependencies(PackageIdentity package,
        NuGetFramework framework,
        SourceCacheContext cacheContext,
        ILogger logger,
        IEnumerable<SourceRepository> repositories,
        ISet<SourcePackageDependencyInfo> availablePackages,
        CancellationToken cancellationToken = default)
    {
        if (availablePackages.Contains(package) || IsCoreLibraries(package.Id)) return;

        foreach (SourceRepository sourceRepository in repositories)
        {
            DependencyInfoResource dependencyInfoResource
                = await sourceRepository.GetResourceAsync<DependencyInfoResource>(cancellationToken)
                    .ConfigureAwait(false);

            SourcePackageDependencyInfo dependencyInfo
                = await dependencyInfoResource.ResolvePackage(
                    package, framework, cacheContext, logger, cancellationToken)
                        .ConfigureAwait(false);

            if (dependencyInfo == null) continue;

            if (dependencyInfo.Dependencies.Any(x => IsCoreLibraries(x.Id)))
            {
                dependencyInfo = new SourcePackageDependencyInfo(
                    dependencyInfo,
                    dependencyInfo.Dependencies.Where(x => !IsCoreLibraries(x.Id)),
                    dependencyInfo.Listed,
                    dependencyInfo.Source,
                    dependencyInfo.DownloadUri,
                    dependencyInfo.PackageHash);
            }

            availablePackages.Add(dependencyInfo);
            foreach (PackageDependency? dependency in dependencyInfo.Dependencies)
            {
                await GetPackageDependencies(
                    new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion),
                    framework,
                    cacheContext,
                    logger,
                    repositories,
                    availablePackages,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public static void GetPackageDependencies(
        PackageDependencyInfo package,
        NuGetFramework framework,
        ISet<PackageDependencyInfo> availablePackages)
    {
        if (availablePackages.Contains(package) || IsCoreLibraries(package.Id)) return;

        availablePackages.Add(package);

        foreach (var dependency in package.Dependencies)
        {
            var dependentPackage = new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion);
            var path = PackagePathResolver.GetInstalledPath(dependentPackage);
            if (path != null)
            {
                var reader = new PackageFolderReader(path);

                var deps = reader.GetPackageDependencies();
                var nearest = FrameworkReducer.GetNearest(
                    framework,
                    deps.Select(x => x.TargetFramework));

                GetPackageDependencies(
                    new PackageDependencyInfo(
                        dependentPackage,
                        deps.Where(x => x.TargetFramework == nearest)
                            .SelectMany(x => x.Packages)),
                    framework, availablePackages);
            }
        }
    }

    public static string GetNupkgFilePath(string packageId, string version)
    {
        return Path.Combine(LocalSourcePath, $"{packageId}.{version}.nupkg");
    }

    public static string GetNuspecFilePath(string packageId, string version)
    {
        return Path.Combine(InstallPath, $"{packageId}.{version}", $"{packageId}.{version}.nuspec");
    }

    public static bool IsCoreLibraries(string name)
    {
        return name is "Beutl.Sdk"
            or "Beutl.Configuration"
            or "Beutl.Controls"
            or "Beutl.Core"
            or "Beutl.Framework"
            or "Beutl.Graphics"
            or "Beutl.Language"
            or "Beutl.Operators"
            or "Beutl.ProjectSystem"
            or "Beutl.Threading"
            or "Beutl.Utilities";
    }

    public static T? TryGetOrDefault<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch
        {
            return default;
        }
    }

    public static async Task<T?> TryGetOrDefault<T>(Func<Task<T>> func)
    {
        try
        {
            return await func().ConfigureAwait(false);
        }
        catch
        {
            return default;
        }
    }

    public static LocalPackage ReadLocalPackageFromNuspecFile(Stream stream)
    {
        return new LocalPackage(new NuspecReader(stream));
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
