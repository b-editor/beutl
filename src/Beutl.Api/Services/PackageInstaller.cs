using Beutl.Api.Objects;

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public partial class PackageInstaller : IBeutlApiResource
{
    private static readonly Mutex s_mutex = new(false, "Beutl.PackageInstaller");


    private readonly HttpClient _httpClient;
    private readonly InstalledPackageRepository _installedPackageRepository;

    private readonly ISettings _settings;
    private readonly PackageSourceProvider _packageSourceProvider;
    private readonly SourceRepositoryProvider _sourceRepositoryProvider;
    private readonly SourceCacheContext _cacheContext;
    private readonly PackageResolver _resolver;

    public PackageInstaller(HttpClient httpClient, InstalledPackageRepository installedPackageRepository)
    {
        _httpClient = httpClient;
        _installedPackageRepository = installedPackageRepository;

        _settings = Settings.LoadDefaultSettings(Helper.AppRoot);
        _packageSourceProvider = new PackageSourceProvider(_settings);
        var localPkgSource = new PackageSource(Helper.LocalSourcePath);
        _packageSourceProvider.AddPackageSource(localPkgSource);

        _sourceRepositoryProvider = new SourceRepositoryProvider(_packageSourceProvider, Repository.Provider.GetCoreV3());
        _cacheContext = new SourceCacheContext()
        {
            DirectDownload = true
        };

        _resolver = new PackageResolver();
    }

    private static void CreateLocalSourceDirectory()
    {
        if (!Directory.Exists(Helper.LocalSourcePath))
        {
            Directory.CreateDirectory(Helper.LocalSourcePath);
        }
    }

    private static string GetSpecFilePath(string packageId, string version)
    {
        return Path.Combine(Helper.LocalSourcePath, $"{packageId}.{version}.nupkg");
    }

    private static Task WaitAny(params WaitHandle[] waitHandles)
    {
        return Task.Run(() => WaitHandle.WaitAny(waitHandles));
    }

    public async Task<PackageInstallContext> PrepareForInstall(
        Release release,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            if (!force && _installedPackageRepository.ExistsPackage(GetSpecFilePath(release.Package.Name, release.Version.Value)))
            {
                throw new Exception("This package is already installed.");
            }

            Asset asset = await release.GetAssetAsync();

            return new PackageInstallContext(release.Package, release, asset);
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }

    public async Task DownloadPackageFile(
        PackageInstallContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            context.Phase = PackageInstallPhase.Downloading;
            CreateLocalSourceDirectory();

            string name = context.Package.Name;
            string version = context.Release.Version.Value;
            string downloadUrl = context.Asset.DownloadUrl;
            using (FileStream destination = File.Create(GetSpecFilePath(name, version)))
            {
                await Download(downloadUrl, destination, progress, cancellationToken);
            }

            context.Phase = PackageInstallPhase.Downloaded;
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }

    public async Task ResolveDependencies(
        PackageInstallContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            context.Phase = PackageInstallPhase.ResolvingDependencies;

            string packageId = context.Package.Name;
            string version = context.Release.Version.Value;
            NuGetFramework nuGetFramework = Helper.GetFrameworkName();
            var package = new PackageIdentity(packageId, NuGetVersion.Parse(version));

#if DEBUG
            logger ??= new ConsoleLogger();
#else
        logger ??= NullLogger.Instance;
#endif

            IEnumerable<SourceRepository> repositories = _sourceRepositoryProvider.GetRepositories();
            var availablePackages = new HashSet<SourcePackageDependencyInfo>(PackageIdentityComparer.Default);
            await Helper.GetPackageDependencies(
                package,
                nuGetFramework,
                _cacheContext,
                logger,
                repositories,
                availablePackages,
                cancellationToken);

            var resolverContext = new PackageResolverContext(
                DependencyBehavior.Lowest,
                new[] { packageId },
                Enumerable.Empty<string>(),
                Enumerable.Empty<PackageReference>(),
                Enumerable.Empty<PackageIdentity>(),
                availablePackages,
                repositories.Select(s => s.PackageSource),
                logger);

            SourcePackageDependencyInfo[] packagesToInstall
                = _resolver.Resolve(resolverContext, cancellationToken)
                    .Select(p => availablePackages.Single(x => PackageIdentityComparer.Default.Equals(x, p)))
                    .ToArray();

            var packageExtractionContext = new PackageExtractionContext(
                PackageSaveMode.Nuspec | PackageSaveMode.Files,
                XmlDocFileSaveMode.None,
                ClientPolicyContext.GetClientPolicy(_settings, logger),
                logger);

            var installedPaths = new List<string>(packagesToInstall.Length);
            foreach (SourcePackageDependencyInfo packageToInstall in packagesToInstall)
            {
                string installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall);
                if (installedPath != null)
                {
                    installedPaths.Add(installedPath);
                }
                else
                {
                    DownloadResource downloadResource = await packageToInstall.Source.GetResourceAsync<DownloadResource>(cancellationToken);
                    using DownloadResourceResult downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                        packageToInstall,
                        new PackageDownloadContext(_cacheContext),
                        SettingsUtility.GetGlobalPackagesFolder(_settings),
                        logger, cancellationToken);

                    await PackageExtractor.ExtractPackageAsync(
                            downloadResult.PackageSource,
                            downloadResult.PackageStream,
                            Helper.PackagePathResolver,
                            packageExtractionContext,
                            cancellationToken);

                    installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall);
                    if (installedPath != null)
                    {
                        installedPaths.Add(installedPath);
                    }
                }
            }

            context.Phase = PackageInstallPhase.ResolvedDependencies;
            context.InstalledPaths = installedPaths;
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }

    private async Task Download(
        string url,
        Stream destination,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        using (HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            long? contentLength = response.Content.Headers.ContentLength;

            using (Stream download = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                if (!contentLength.HasValue)
                {
                    progress.Report(double.PositiveInfinity);
                    await download.CopyToAsync(destination, cancellationToken);
                }
                else
                {
                    int bufferSize = 81920;
                    byte[] buffer = new byte[bufferSize];
                    long totalBytesRead = 0;
                    int bytesRead;
                    while ((bytesRead = await download.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        totalBytesRead += bytesRead;
                        progress.Report(totalBytesRead / (double)contentLength.Value);
                    }
                }
            }
        }
    }
}
