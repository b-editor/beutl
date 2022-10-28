using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api.Objects;

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;

using Reactive.Bindings;

namespace Beutl.Api.Services;

public class PackageInstaller
{
    private readonly HttpClient _httpClient;
    private readonly InstalledPackageRepository _installedPackageRepository;

    private readonly ISettings settings;
    private readonly PackageSourceProvider packageSourceProvider;
    private readonly SourceRepositoryProvider sourceRepositoryProvider;
    private readonly SourceCacheContext cacheContext;
    private readonly PackageResolver resolver;

    public PackageInstaller(HttpClient httpClient, InstalledPackageRepository installedPackageRepository)
    {
        _httpClient = httpClient;
        _installedPackageRepository = installedPackageRepository;

        settings = Settings.LoadDefaultSettings(Helper.AppRoot);
        packageSourceProvider = new PackageSourceProvider(settings);
        var localPkgSource = new PackageSource(Helper.LocalSourcePath);
        packageSourceProvider.AddPackageSource(localPkgSource);

        sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
        cacheContext = new SourceCacheContext()
        {
            DirectDownload = true
        };

        resolver = new PackageResolver();
    }

    public async Task Install(Package package, Release release, Asset asset, IPackageInstallProgress reporter, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Helper.LocalSourcePath))
        {
            Directory.CreateDirectory(Helper.LocalSourcePath);
        }

        var name = package.Name;
        var version = release.Version.Value;
        using (var destination = File.Create(Path.Combine(Helper.LocalSourcePath, $"{name}.{version}.nupkg")))
        {
            await Download(asset.DownloadUrl, destination, reporter, cancellationToken);

            await ResolveDependencies(name, version, Helper.GetFrameworkName(), reporter);


        }
    }

    public async Task Download(
        string url,
        Stream destination,
        IPackageInstallProgress reporter, CancellationToken cancellationToken)
    {
        using (HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            var contentLength = response.Content.Headers.ContentLength;

            using (var download = await response.Content.ReadAsStreamAsync())
            {
                if (!contentLength.HasValue)
                {
                    reporter.Indeterminate(IPackageInstallProgress.ActionType.Downloading);
                    await download.CopyToAsync(destination);
                }
                else
                {
                    var bufferSize = 81920;
                    var buffer = new byte[bufferSize];
                    long totalBytesRead = 0;
                    int bytesRead;
                    while ((bytesRead = await download.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        totalBytesRead += bytesRead;
                        reporter.Download(totalBytesRead, contentLength.Value);
                    }
                }

                reporter.DownloadComplete();
            }
        }
    }

    public async Task ResolveDependencies(
        string packageId,
        string version,
        NuGetFramework nuGetFramework,
        IPackageInstallProgress reporter)
    {
        var package = new PackageIdentity(packageId, NuGetVersion.Parse(version));

        var logger = new ConsoleLogger();

        reporter.Indeterminate(IPackageInstallProgress.ActionType.ResolvingDependencies);
        var repositories = sourceRepositoryProvider.GetRepositories();
        var availablePackages = new HashSet<SourcePackageDependencyInfo>(PackageIdentityComparer.Default);
        await Helper.GetPackageDependencies(
            new PackageIdentity(packageId, NuGetVersion.Parse(version)),
            nuGetFramework,
            cacheContext,
            logger,
            repositories,
            availablePackages);

        var resolverContext = new PackageResolverContext(
            DependencyBehavior.Lowest,
            new[] { packageId },
            Enumerable.Empty<string>(),
            Enumerable.Empty<PackageReference>(),
            Enumerable.Empty<PackageIdentity>(),
            availablePackages,
            repositories.Select(s => s.PackageSource),
            logger);

        IEnumerable<SourcePackageDependencyInfo> packagesToInstall
            = resolver.Resolve(resolverContext, CancellationToken.None)
                .Select(p => availablePackages.Single(x => PackageIdentityComparer.Default.Equals(x, p)));

        var packageExtractionContext = new PackageExtractionContext(
            PackageSaveMode.Nuspec | PackageSaveMode.Files,
            XmlDocFileSaveMode.None,
            ClientPolicyContext.GetClientPolicy(settings, logger),
            logger);

        foreach (SourcePackageDependencyInfo packageToInstall in packagesToInstall)
        {
            PackageReaderBase packageReader;
            string installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall);
            if (installedPath == null)
            {
                reporter.Indeterminate(IPackageInstallProgress.ActionType.Downloading);
                DownloadResource downloadResource = await packageToInstall.Source.GetResourceAsync<DownloadResource>(CancellationToken.None);
                DownloadResourceResult downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                    packageToInstall,
                    new PackageDownloadContext(cacheContext),
                    SettingsUtility.GetGlobalPackagesFolder(settings),
                    logger, CancellationToken.None);

                reporter.Indeterminate(IPackageInstallProgress.ActionType.Extracting);
                await PackageExtractor.ExtractPackageAsync(
                        downloadResult.PackageSource,
                        downloadResult.PackageStream,
                        Helper.PackagePathResolver,
                        packageExtractionContext,
                        CancellationToken.None);

                packageReader = downloadResult.PackageReader;
            }
        }
    }

    public string[] UnnecessaryPackages()
    {
        if (!Directory.Exists(Helper.InstallPath))
        {
            return Array.Empty<string>();
        }

        NuGetFramework framework = Helper.GetFrameworkName();

        var logger = new ConsoleLogger();

        var availablePackages = new HashSet<PackageDependencyInfo>(PackageIdentityComparer.Default);

        foreach (string item in _installedPackageRepository.GetLocalPackages())
        {
            var reader = new NuspecReader(item);
            PackageIdentity packageId = reader.GetIdentity();

            var deps = reader.GetDependencyGroups();
            var nearest = Helper.FrameworkReducer.GetNearest(
                framework,
                deps.Select(x => x.TargetFramework));

            Helper.GetPackageDependencies(
                new PackageDependencyInfo(packageId, deps
                    .Where(x => x.TargetFramework == nearest)
                    .SelectMany(x => x.Packages)),
                framework,
                logger,
                availablePackages);
        }

        string[] directories = Directory.GetDirectories(Helper.InstallPath);

        return directories.ExceptBy(
            availablePackages.Select(x => $"{x.Id}.{x.Version}"),
            y => Path.GetDirectoryName(y))
            .ToArray();
    }
}
