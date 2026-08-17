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
    private Task? _disposeTask;
    private bool _disposed;
    private bool _drained;
    private static readonly AsyncLocal<PackageInstaller?> s_transactionOwner = new();

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

        const string ConfigFileName = "nuget.config";
        string configPath = Path.Combine(Helper.AppRoot, ConfigFileName);
        if (File.Exists(configPath))
        {
            using (StreamReader reader = File.OpenText(configPath))
            {
                while (reader.ReadLine() is string line)
                {
                    if (line.Contains("<clear"))
                    {
                        goto LoadSettings;
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

    LoadSettings:
        //_settings = Settings.LoadDefaultSettings(Helper.AppRoot);
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

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        long deadline = Environment.TickCount64 + 30_000;
        bool drained = false;
        while (Environment.TickCount64 < deadline)
        {
            Task[] operations;
            lock (_gate)
            {
                operations = _operations.ToArray();
            }

            if (operations.Length == 0)
            {
                drained = true;
                break;
            }

            try
            {
                await Task.WhenAll(operations).ConfigureAwait(false);
            }
            catch
            {
            }

            lock (_gate)
            {
                _operations.RemoveWhere(task => task.IsCompleted);
            }
        }

        if (!drained)
        {
            _logger.LogWarning("Package installer did not drain within the shutdown deadline.");
        }

        lock (_gate)
        {
            _drained = true;
        }

        _cacheContext.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private Task TrackAsync(Func<Task> operation)
        => TrackAsyncCore(operation);

    private Task<T> TrackAsync<T>(Func<Task<T>> operation)
        => TrackAsyncCore(operation);

    public Task TrackInstallOperationAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        TaskCompletionSource proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task task = proxy.Task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _operations.RemoveWhere(task => task.IsCompleted);
            // Register before invoking so a re-entrant DisposeAsync drains this transaction.
            _operations.Add(task);
            // Run in the caller context; the operation's awaits use ConfigureAwait(false)
            // so shutdown can block without deadlocking.
            _ = RunTransactionAsync(operation, proxy);
        }

        return task;
    }

    private async Task RunTransactionAsync(Func<Task> operation, TaskCompletionSource proxy)
    {
        PackageInstaller? previous = s_transactionOwner.Value;
        s_transactionOwner.Value = this;
        try
        {
            await operation().ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (Exception ex)
        {
            // Propagate the failure so the caller reports it and queues fallback.
            proxy.TrySetException(ex);
        }
        finally
        {
            s_transactionOwner.Value = previous;
        }
    }

    private Task TrackAsyncCore(Func<Task> operation)
    {
        TaskCompletionSource proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            // After draining, reject all work including nested phases.
            if (_drained || (_disposed && !ReferenceEquals(s_transactionOwner.Value, this)))
            {
                throw new ObjectDisposedException(nameof(PackageInstaller));
            }
            _operations.RemoveWhere(task => task.IsCompleted);
            // Register before invoking so a concurrent DisposeAsync drains this phase.
            _operations.Add(proxy.Task);
        }

        // Invoke outside the lock so a re-entrant delegate cannot deadlock on _gate.
        _ = RunTrackedAsync(operation, proxy);
        return proxy.Task;
    }

    private Task<T> TrackAsyncCore<T>(Func<Task<T>> operation)
    {
        TaskCompletionSource<T> proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_drained || (_disposed && !ReferenceEquals(s_transactionOwner.Value, this)))
            {
                throw new ObjectDisposedException(nameof(PackageInstaller));
            }
            _operations.RemoveWhere(task => task.IsCompleted);
            // Register before invoking so a concurrent DisposeAsync drains this phase.
            _operations.Add(proxy.Task);
        }

        // Invoke outside the lock so a re-entrant delegate cannot deadlock on _gate.
        _ = RunTrackedAsync(operation, proxy);
        return proxy.Task;
    }

    private async Task RunTrackedAsync(Func<Task> operation, TaskCompletionSource proxy)
    {
        try
        {
            await operation().ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            proxy.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    private async Task<T> RunTrackedAsync<T>(Func<Task<T>> operation, TaskCompletionSource<T> proxy)
    {
        try
        {
            T result = await operation().ConfigureAwait(false);
            proxy.TrySetResult(result);
            return result;
        }
        catch (OperationCanceledException ex)
        {
            proxy.TrySetCanceled(ex.CancellationToken);
            return default!;
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
            return default!;
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

        if (!force && _installedPackageRepository.ExistsPackage(name, version))
        {
            throw new Exception("This package is already installed.");
        }

        if (_installingContexts.TryGetValue(packageId, out PackageInstallContext? context))
        {
            return context;
        }
        else
        {
            var asset = await release.GetAssetAsync(cancellationToken).ConfigureAwait(false);

            context = new PackageInstallContext(name, version, asset.DownloadUrl)
            {
                Asset = asset
            };
            _installingContexts.Add(packageId, context);
            return context;
        }
    }

    public PackageInstallContext PrepareForInstall(
        string name,
        string version,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageId = new PackageIdentity(name, new NuGetVersion(version));

        if (!force && _installedPackageRepository.ExistsPackage(name, version))
        {
            throw new Exception("This package is already installed.");
        }

        if (_installingContexts.TryGetValue(packageId, out PackageInstallContext? context))
        {
            return context;
        }
        else
        {
            context = new PackageInstallContext(name, version, string.Empty)
            {
                Phase = PackageInstallPhase.Downloaded
            };
            _installingContexts.Add(packageId, context);
            return context;
        }
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
            using (FileStream destination = File.Create(context.NuGetPackageFile))
            {
                await Download(downloadUrl, destination, progress, cancellationToken).ConfigureAwait(false);
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
        async Task<bool> Varify(HashAlgorithm algorithm, Stream stream, long totalLength, string hashValue)
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

        static string ByteArrayToString(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte item in bytes.AsSpan())
            {
                sb.Append($"{item:X2}");
            }

            return sb.ToString();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if ((int)context.Phase <= (int)PackageInstallPhase.Verifying)
        {
            context.Phase = PackageInstallPhase.Verifying;
            if (context.Asset is { } asset
                && context.NuGetPackageFile != null)
            {
                using FileStream stream = File.OpenRead(context.NuGetPackageFile);
                using var sha256 = SHA256.Create();
                using var sha384 = SHA384.Create();
                using var sha512 = SHA512.Create();
                (HashAlgorithm, string?)[] items =
                [
                    (sha256, asset.Sha256)
                ];

                long totalLength = items.Count(x => !string.IsNullOrWhiteSpace(x.Item2)) * stream.Length;
                if (totalLength == 0)
                {
                    context.HashVerified = false;
                    return;
                }

                foreach ((HashAlgorithm algorithm, string? hash) in items)
                {
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        stream.Position = 0;
                        if (!await Varify(algorithm, stream, totalLength, hash))
                        {
                            context.HashVerified = false;
                            return;
                        }
                    }
                }

                context.HashVerified = true;
                context.Phase = PackageInstallPhase.Verified;
            }
        }
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
        var context = PrepareForInstall(
            package.Id, package.Version.ToString(), force: true, cancellationToken);
        // Call the core directly; the outer operation is already admitted.
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
                    repositories,
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

                    string? installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall);
                    if (installedPath != null)
                    {
                        installedPaths.Add(installedPath);
                    }
                    else
                    {
                        DownloadResource downloadResource = await packageToInstall.Source.GetResourceAsync<DownloadResource>(cancellationToken).ConfigureAwait(false);
                        using DownloadResourceResult downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                            packageToInstall,
                            new PackageDownloadContext(_cacheContext),
                            SettingsUtility.GetGlobalPackagesFolder(_settings),
                            logger, cancellationToken)
                            .ConfigureAwait(false);

                        await PackageExtractor.ExtractPackageAsync(
                            downloadResult.PackageSource,
                            downloadResult.PackageStream,
                            Helper.PackagePathResolver,
                            packageExtractionContext,
                            cancellationToken)
                            .ConfigureAwait(false);

                        installedPath = Helper.PackagePathResolver.GetInstalledPath(packageToInstall);
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

                            installedPaths.Add(installedPath);
                        }
                    }
                }

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

    private async Task Download(
        string url,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_apiApplication.AuthenticatedUser.Value is { } user)
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

        using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            long? contentLength = response.Content.Headers.ContentLength;

            using (Stream download = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
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
    }

}
