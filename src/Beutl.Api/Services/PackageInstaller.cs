using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using Beutl.Api.Objects;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;
using ILogger = NuGet.Common.ILogger;

namespace Beutl.Api.Services;

public partial class PackageInstaller : IBeutlApiResource, IAsyncDisposable
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger = Log.CreateLogger<PackageInstaller>();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly BeutlApiApplication _apiApplication;

    private readonly ISettings _settings;
    private readonly PackageSourceProvider _packageSourceProvider;
    private readonly SourceRepositoryProvider _sourceRepositoryProvider;
    private readonly SourceCacheContext _cacheContext;
    private readonly PackageResolver _resolver;

    private readonly Dictionary<PackageIdentity, PackageInstallContext> _installingContexts = [];
    private readonly object _gate = new();
    private readonly HashSet<Task> _operations = [];
    private readonly Dictionary<Task, ShutdownFallback> _shutdownFallbacks = [];
    private Task? _disposeTask;
    private bool _disposed;
    private bool _drained;
    private volatile bool _shutdownFallbackPublicationEnabled;
    private static readonly AsyncLocal<PackageInstaller?> s_transactionOwner = new();

    internal Action? BeforeShutdownFallbackSnapshot { get; set; }

    internal Action? AfterSuccessfulInstallFallbackDisarmed { get; set; }

    private const string DefaultNuGetConfigContentTemplate = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""Beutl Local Packages"" value=""{0}"" />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" protocolVersion=""3"" />
  </packageSources>
</configuration>
";

    public PackageInstaller(HttpClient httpClient, InstalledPackageRepository installedPackageRepository, BeutlApiApplication apiApplication)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
        _installedPackageRepository = installedPackageRepository;
        _apiApplication = apiApplication;

        RecoverDataPackageInstalls(installedPackageRepository);

        const string ConfigFileName = "nuget.config";
        EnsureNuGetConfig(Path.Combine(Helper.AppRoot, ConfigFileName));

        _settings = new Settings(Helper.AppRoot, ConfigFileName);
        _packageSourceProvider = new PackageSourceProvider(_settings);

        _sourceRepositoryProvider = new SourceRepositoryProvider(_packageSourceProvider, Repository.Provider.GetCoreV3());
        _cacheContext = new SourceCacheContext()
        {
            DirectDownload = true
        };

        _resolver = new PackageResolver();
    }

    internal PackageInstaller(
        HttpClient httpClient,
        bool ownsHttpClient,
        InstalledPackageRepository installedPackageRepository,
        BeutlApiApplication apiApplication)
        : this(httpClient, installedPackageRepository, apiApplication)
    {
        _ownsHttpClient = ownsHttpClient;
    }

    private static void EnsureNuGetConfig(string configPath)
    {
        if (File.Exists(configPath))
        {
            using (StreamReader reader = File.OpenText(configPath))
            {
                while (reader.ReadLine() is string line)
                {
                    if (line.Contains("<clear"))
                    {
                        return;
                    }
                }
            }

            File.Delete(configPath);
        }

        if (!File.Exists(configPath))
        {
            using (StreamWriter writer = File.CreateText(configPath))
            {
                writer.Write(string.Format(DefaultNuGetConfigContentTemplate, Helper.LocalSourcePath));
            }
        }
    }

    private static void CreateLocalSourceDirectory()
    {
        if (!Directory.Exists(Helper.LocalSourcePath))
        {
            Directory.CreateDirectory(Helper.LocalSourcePath);
        }
    }

    public Task<PackageInstallContext> PrepareForInstall(
        Release release,
        bool force = false,
        CancellationToken cancellationToken = default)
        => TrackAsync(() => PrepareForInstallCoreAsync(release, force, cancellationToken));

    private async Task<PackageInstallContext> PrepareForInstallCoreAsync(
        Release release,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string name = release.Package.Name;
        string version = release.Version.Value;
        var packageId = new PackageIdentity(name, new NuGetVersion(version));

        PackageInstallContext? context = FindPreparedContext(packageId, name, version, force);
        if (context is not null)
        {
            return context;
        }

        var asset = await release.GetAssetAsync(cancellationToken).ConfigureAwait(false);

        context = new PackageInstallContext(name, version, asset.DownloadUrl)
        {
            Asset = asset
        };
        _installingContexts.Add(packageId, context);
        return context;
    }

    public PackageInstallContext PrepareForInstall(
        string name,
        string version,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        return TrackSyncOperation(() => PrepareForInstallCore(name, version, force, cancellationToken));
    }

    private PackageInstallContext PrepareForInstallCore(
        string name,
        string version,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageId = new PackageIdentity(name, new NuGetVersion(version));

        PackageInstallContext? context = FindPreparedContext(packageId, name, version, force);
        if (context is not null)
        {
            return context;
        }

        context = new PackageInstallContext(name, version, string.Empty)
        {
            Phase = PackageInstallPhase.Downloaded
        };
        _installingContexts.Add(packageId, context);
        return context;
    }

    // Refuses an installed package unless forced, and hands back the context an earlier preparation left.
    private PackageInstallContext? FindPreparedContext(
        PackageIdentity packageId,
        string name,
        string version,
        bool force)
    {
        if (!force && _installedPackageRepository.ExistsPackage(name, version))
        {
            throw new Exception("This package is already installed.");
        }

        return _installingContexts.TryGetValue(packageId, out PackageInstallContext? context) ? context : null;
    }

    public Task DownloadPackageFile(
        PackageInstallContext context,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => TrackAsync(() => DownloadPackageFileCoreAsync(context, progress, cancellationToken));

    private async Task DownloadPackageFileCoreAsync(
        PackageInstallContext context,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((int)context.Phase <= (int)PackageInstallPhase.Downloading)
        {
            context.Phase = PackageInstallPhase.Downloading;
            CreateLocalSourceDirectory();

            string name = context.PackageName;
            string version = context.Version;
            string downloadUrl = context.DownloadUrl;
            context.NuGetPackageFile = Helper.GetNupkgFilePath(name, version);
            string temporaryPath = context.NuGetPackageFile + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (FileStream destination = File.Create(temporaryPath))
                {
                    await Download(downloadUrl, destination, progress, cancellationToken).ConfigureAwait(false);
                    destination.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, context.NuGetPackageFile, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            context.Phase = PackageInstallPhase.Downloaded;
        }
    }

    public Task VerifyPackageFile(
        PackageInstallContext context,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => TrackAsync(() => VerifyPackageFileCoreAsync(context, progress, cancellationToken));

    private async Task VerifyPackageFileCoreAsync(
        PackageInstallContext context,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((int)context.Phase > (int)PackageInstallPhase.Verifying)
            return;

        context.Phase = PackageInstallPhase.Verifying;
        if (context.Asset is not { } asset
            || context.NuGetPackageFile == null)
            return;

        using FileStream stream = File.OpenRead(context.NuGetPackageFile);
        using var sha256 = SHA256.Create();
        (HashAlgorithm, string?)[] items =
        [
            (sha256, asset.Sha256)
        ];

        long totalLength = items.Count(x => !string.IsNullOrWhiteSpace(x.Item2)) * stream.Length;
        if (items.All(item => string.IsNullOrWhiteSpace(item.Item2)))
        {
            context.HashVerified = false;
            return;
        }

        foreach ((HashAlgorithm algorithm, string? hash) in items)
        {
            if (!string.IsNullOrWhiteSpace(hash))
            {
                stream.Position = 0;
                if (!await VerifyHashAsync(algorithm, stream, totalLength, hash, progress, cancellationToken))
                    throw RejectDownloadedPackage(context, context.NuGetPackageFile, stream);
            }
        }

        context.HashVerified = true;
        context.Phase = PackageInstallPhase.Verified;
    }

    private static async Task<bool> VerifyHashAsync(
        HashAlgorithm algorithm,
        Stream stream,
        long totalLength,
        string hashValue,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        long length = stream.Length;
        int bufferSize = 81920;
        byte[] buffer = new byte[bufferSize];
        long totalBytesRead = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            totalBytesRead += bytesRead;
            if (totalBytesRead < length)
            {
                algorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            else
            {
                algorithm.TransformFinalBlock(buffer, 0, bytesRead);
            }

            progress?.Report(totalBytesRead / (double)totalLength);
        }

        if (totalBytesRead == 0)
            algorithm.TransformFinalBlock([], 0, 0);

        if (algorithm.Hash == null)
        {
            return false;
        }
        else
        {
            string computedHash = ByteArrayToString(algorithm.Hash);
            return StringComparer.OrdinalIgnoreCase.Equals(computedHash, hashValue);
        }
    }

    private static string ByteArrayToString(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte item in bytes.AsSpan())
        {
            sb.Append($"{item:X2}");
        }

        return sb.ToString();
    }

    private InvalidDataException RejectDownloadedPackage(
        PackageInstallContext context,
        string packageFile,
        FileStream stream)
    {
        context.HashVerified = false;
        // Do not leave a rejected download where the local-package path
        // can later load it without the server's advertised digest.
        stream.Dispose();
        File.Delete(packageFile);
        var identity = new PackageIdentity(context.PackageName, NuGetVersion.Parse(context.Version));
        if (_installingContexts.TryGetValue(identity, out PackageInstallContext? cached)
            && ReferenceEquals(cached, context))
            _installingContexts.Remove(identity);
        return new InvalidDataException("The downloaded package does not match its advertised hash.");
    }

    public Task ReResolveDependencies(
        PackageIdentity package,
        ILogger? logger,
        CancellationToken cancellationToken = default)
        => TrackAsync(() => ReResolveDependenciesCoreAsync(package, logger, cancellationToken));

    private async Task ReResolveDependenciesCoreAsync(
        PackageIdentity package,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // Call the core directly; the outer operation is already admitted.
        var context = PrepareForInstallCore(
            package.Id, package.Version.ToString(), force: true, cancellationToken);
        await ResolveDependenciesCoreAsync(context, logger, cancellationToken);
    }

    public Task ResolveDependencies(
        PackageInstallContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
        => TrackAsync(() => ResolveDependenciesCoreAsync(context, logger, cancellationToken));

    private async Task ResolveDependenciesCoreAsync(
        PackageInstallContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        PackageIdentity? package = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((int)context.Phase <= (int)PackageInstallPhase.ResolvingDependencies)
            {
                context.Phase = PackageInstallPhase.ResolvingDependencies;

                string packageId = context.PackageName;
                string version = context.Version;
                NuGetFramework nuGetFramework = Helper.GetFrameworkName();
                package = new PackageIdentity(packageId, NuGetVersion.Parse(version));

                logger ??= new LoggerAdapter(_logger);

                IEnumerable<SourceRepository> repositories = _sourceRepositoryProvider.GetRepositories();
                var availablePackages = new HashSet<SourcePackageDependencyInfo>(PackageIdentityComparer.Default);
                await Helper.GetPackageDependencies(
                    package,
                    nuGetFramework,
                    _cacheContext,
                    logger,
                    // Installed packages come first. Re-resolving after a Beutl update must work from them:
                    // PackageTools.UI deletes the downloaded nupkg from the local source, and store packages
                    // are not on nuget.org.
                    [CreateInstalledPackagesRepository(), .. repositories],
                    availablePackages,
                    cancellationToken)
                    .ConfigureAwait(false);

                var resolverContext = new PackageResolverContext(
                    DependencyBehavior.Lowest,
                    [packageId],
                    [],
                    [],
                    CoreLibraries.GetPreferredVersions(),
                    availablePackages,
                    repositories.Select(s => s.PackageSource),
                    logger);

                SourcePackageDependencyInfo[] packagesToInstall
                    = _resolver.Resolve(resolverContext, cancellationToken)
                        .Select(p => availablePackages.Single(x => PackageIdentityComparer.Default.Equals(x, p)))
                        .ToArray();

                var packageExtractionContext = new PackageExtractionContext(
                    PackageSaveMode.Defaultv3,
                    XmlDocFileSaveMode.None,
                    ClientPolicyContext.GetClientPolicy(_settings, logger),
                    logger);

                var installedPaths = new List<string>(packagesToInstall.Length);
                foreach (SourcePackageDependencyInfo packageToInstall in packagesToInstall)
                {
                    // Beutl.Sdkに含まれるライブラリの場合、飛ばす。
                    if (CoreLibraries.IncludedInPackageDependencies(packageToInstall.Id, packageToInstall.Version))
                    {
                        continue;
                    }

                    string? installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall)
                        ?? await InstallResolvedPackageAsync(
                                packageToInstall,
                                context,
                                packageExtractionContext,
                                logger,
                                cancellationToken)
                            .ConfigureAwait(false);
                    if (installedPath != null)
                    {
                        installedPaths.Add(installedPath);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                ResolvedPackageDependencies.Save(Helper.ResolveInstalledDirectory(package), nuGetFramework,
                    packagesToInstall.Where(item => !CoreLibraries.IncludedInPackageDependencies(item.Id, item.Version)));
                context.Phase = PackageInstallPhase.ResolvedDependencies;
                context.InstalledPaths = installedPaths;
            }
        }
        finally
        {
            if (package is { })
            {
                _installingContexts.Remove(package);
            }
        }
    }

    // Each installed package folder keeps its own copy of the nupkg, so installed packages
    // can be resolved without the local source or the network.
    private SourceRepository CreateInstalledPackagesRepository()
    {
        return _sourceRepositoryProvider.CreateRepository(
            new PackageSource(Helper.InstallPath, "Beutl Installed Packages"),
            NuGet.Protocol.FeedType.FileSystemPackagesConfig);
    }

    // Downloads and extracts a resolved package that is not installed yet; returns where it landed, or null.
    private async Task<string?> InstallResolvedPackageAsync(
        SourcePackageDependencyInfo packageToInstall,
        PackageInstallContext context,
        PackageExtractionContext packageExtractionContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Helper.GetPackageDependencies only collects packages resolved from a repository.
        SourceRepository source = packageToInstall.Source
            ?? throw new InvalidOperationException(
                $"'{packageToInstall.Id} {packageToInstall.Version}' has no package source.");
        DownloadResource downloadResource
            = await source.GetResourceAsync<DownloadResource>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"'{source.PackageSource.Source}' cannot download packages.");
        using DownloadResourceResult downloadResult = await downloadResource.GetDownloadResourceResultAsync(
            packageToInstall,
            new PackageDownloadContext(_cacheContext),
            SettingsUtility.GetGlobalPackagesFolder(_settings),
            logger, cancellationToken)
            .ConfigureAwait(false);

        await ExtractDownloadedPackageAsync(
            downloadResult,
            packageToInstall,
            source.PackageSource.Source,
            packageExtractionContext,
            cancellationToken)
            .ConfigureAwait(false);

        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall);
        if (installedPath != null)
        {
            var reader = new PackageFolderReader(installedPath);
            NuspecReader nuspec = reader.NuspecReader;

            // GetLicenseMetadataの戻り値はNullの可能性があるので、
            // https://github.com/NuGet/NuGet.Client/blob/e873b496daa6839a86f4b820d15945a9aad98e3d/src/NuGet.Core/NuGet.Packaging/NuspecReader.cs#L434
            if (nuspec.GetRequireLicenseAcceptance()
                && nuspec.GetLicenseMetadata() is { } license)
            {
                context.LicensesRequiringApproval.Add((packageToInstall, license));
            }
        }

        return installedPath;
    }

    // NuGet reports a package it could not find, or a cancelled download, as a result with neither a
    // stream nor a reader instead of throwing. A plugin-backed source hands over an available package
    // as a reader without a stream.
    internal static async Task ExtractDownloadedPackageAsync(
        DownloadResourceResult downloadResult,
        PackageIdentity package,
        string sourceName,
        PackageExtractionContext extractionContext,
        CancellationToken cancellationToken)
    {
        // A package already in the global packages folder comes back without a source, which NuGet
        // treats the same as an empty one.
        string packageSource = downloadResult.PackageSource ?? string.Empty;
        if (downloadResult.PackageStream is { } packageStream)
        {
            await PackageExtractor.ExtractPackageAsync(
                packageSource,
                packageStream,
                Helper.PackagePathResolver,
                extractionContext,
                cancellationToken)
                .ConfigureAwait(false);
        }
        else if (downloadResult.PackageReader is { } packageReader)
        {
            await PackageExtractor.ExtractPackageAsync(
                packageSource,
                packageReader,
                Helper.PackagePathResolver,
                extractionContext,
                cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                $"'{package.Id} {package.Version}' could not be downloaded from '{sourceName}' ({downloadResult.Status}).");
        }
    }

    private async Task Download(
        string url,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var apiOrigin = new Uri(BeutlApiApplication.BaseUrl);
        Uri downloadUri = new(apiOrigin, url);
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        await AuthorizeIfApiOriginAsync(request, apiOrigin, downloadUri, cancellationToken).ConfigureAwait(false);

        using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long? contentLength = response.Content.Headers.ContentLength;

            using (Stream download = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                await CopyWithProgressAsync(download, destination, contentLength, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    // The bearer token only goes to the API origin; a download hosted elsewhere is fetched anonymously.
    private async Task AuthorizeIfApiOriginAsync(
        HttpRequestMessage request,
        Uri apiOrigin,
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        if (downloadUri.Scheme == apiOrigin.Scheme
            && downloadUri.IdnHost == apiOrigin.IdnHost
            && downloadUri.Port == apiOrigin.Port
            && _apiApplication.AuthenticatedUser.Value is { } user)
        {
            try
            {
                await user.RefreshAsync(cancellationToken).ConfigureAwait(false);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh authenticated user. Proceeding without authentication.");
            }
        }
    }

    private static async Task CopyWithProgressAsync(
        Stream download,
        Stream destination,
        long? contentLength,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!contentLength.HasValue)
        {
            progress?.Report(double.PositiveInfinity);
            await download.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
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
                progress?.Report(totalBytesRead / (double)contentLength.Value);
            }
        }
    }

}
