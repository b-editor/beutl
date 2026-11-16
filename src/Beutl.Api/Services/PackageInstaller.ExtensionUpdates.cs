using Beutl.Api.Objects;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Beutl.Api.Services;

internal sealed record StagedExtensionUpdate(PackageIdentity Package, string Source, bool WithAppUpdate);

internal enum ExtensionUpdateStageResult
{
    Ready,
    Skipped,
    RequiresLicenseAcceptance,
}

public partial class PackageInstaller
{
    private const string WithAppDirectory = "app";

    // Both ends of an update are part of its location. A postponed Beutl update or a manually
    // replaced extension cannot apply a download meant for another version.
    private static string ExtensionUpdateDirectory(PackageIdentity installed)
        => Path.Combine(Helper.AppRoot, "extensionUpdates", ValidatePackageName(installed.Id).ToLowerInvariant(), installed.Version.ToNormalizedString());

    internal Task<ExtensionUpdateStageResult> StageExtensionUpdateAsync(PackageIdentity installed, Release release,
        NuGetVersion host, bool appUpdate, CancellationToken token, Func<bool>? isCurrent = null)
        => TrackAsync(async () =>
        {
            var candidate = new PackageIdentity(installed.Id, NuGetVersion.Parse(release.Version.Value));
            string hostDirectory = Path.Combine(ExtensionUpdateDirectory(installed), host.ToNormalizedString());
            string directory = appUpdate ? Path.Combine(hostDirectory, WithAppDirectory) : hostDirectory;
            string name = $"{installed.Id}.{candidate.Version.ToNormalizedString()}";
            string rejected = Path.Combine(hostDirectory, name + ".rejected");
            string ready = Path.Combine(directory, name + ".ready");
            bool CanStage() => (isCurrent?.Invoke() ?? true) && !File.Exists(rejected);
            using (await _apiApplication.Lock.LockAsync(token))
            {
                if (!CanStage()) return ExtensionUpdateStageResult.Skipped;
            }
            if (Directory.Exists(ready)) return await FinishStagingAsync(ready).ConfigureAwait(false);

            // Publish the whole local feed together. Startup never selects a partially downloaded graph.
            string draft = Directory.CreateDirectory(Path.Combine(directory, $".preparing-{Guid.NewGuid():N}")).FullName;
            try
            {
                string archive = Path.Combine(draft, name + ".nupkg");
                string previousArchive = Path.Combine(directory, name + ".nupkg");
                if (File.Exists(previousArchive))
                    File.Copy(previousArchive, archive);
                else
                    await DownloadExtensionArchiveAsync(candidate, release, archive, token).ConfigureAwait(false);

                var source = _sourceRepositoryProvider.CreateRepository(new PackageSource(draft));
                var logger = new LoggerAdapter(_logger);
                var graph = await ResolvePackageGraphAsync(candidate, Helper.GetFrameworkName(), [source, .. GetInstallSources()],
                    null, logger, token, useCurrentHostLibraries: host == NuGetVersion.Parse(BeutlApplication.Version)).ConfigureAwait(false);
                foreach (var dep in graph.Where(dep => !candidate.Equals(dep)))
                {
                    SourceRepository repository = dep.Source ?? throw new InvalidDataException($"'{dep}' has no package source.");
                    using var output = File.Create(Path.Combine(draft, $"{dep.Id}.{dep.Version.ToNormalizedString()}.nupkg"));
                    // The packages.config source supplies the archive path and has no FindPackageByIdResource.
                    if (repository.FeedTypeOverride == FeedType.FileSystemPackagesConfig && dep.DownloadUri is { IsFile: true } archiveUri)
                    {
                        using var input = File.OpenRead(archiveUri.LocalPath);
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                        continue;
                    }
                    var resource = await repository.GetResourceAsync<FindPackageByIdResource>(token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"The source cannot download '{dep}'.");
                    if (!await resource.CopyNupkgToStreamAsync(dep.Id, dep.Version, output, _cacheContext, logger, token).ConfigureAwait(false))
                        throw new InvalidDataException($"Could not prepare dependency '{dep}'.");
                }
                return await FinishStagingAsync(draft).ConfigureAwait(false);
            }
            finally
            {
                DeleteIfExists(draft);
            }

            async Task<ExtensionUpdateStageResult> FinishStagingAsync(string feed)
            {
                bool accepted = AreExtensionLicensesAccepted(Directory.EnumerateFiles(feed, "*.nupkg"), token);
                // A manual change during the download takes precedence over publishing its result.
                using var lease = await _apiApplication.Lock.LockAsync(token);
                token.ThrowIfCancellationRequested();
                if (!CanStage()) return ExtensionUpdateStageResult.Skipped;
                if (!accepted) return ExtensionUpdateStageResult.RequiresLicenseAcceptance;
                if (feed != ready)
                {
                    try
                    {
                        Directory.Move(feed, ready);
                    }
                    catch (IOException) when (Directory.Exists(ready))
                    {
                        // Another Beutl instance published the same update.
                    }
                }
                return ExtensionUpdateStageResult.Ready;
            }
        });

    private bool AreExtensionLicensesAccepted(IEnumerable<string> archives, CancellationToken token)
    {
        foreach (string archive in archives)
        {
            token.ThrowIfCancellationRequested();
            using var reader = new PackageArchiveReader(archive);
            if (reader.NuspecReader.GetRequireLicenseAcceptance()
                && !_apiApplication.GetResource<AcceptedLicenseManager>().Accepted.ContainsKey(reader.GetIdentity()))
                return false;
        }
        return true;
    }

    private async Task DownloadExtensionArchiveAsync(PackageIdentity candidate, Release release, string path, CancellationToken token)
    {
        var asset = await release.GetAssetAsync(token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(asset.Sha256))
            throw new InvalidDataException("The extension update has no SHA-256 digest.");

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await Download(asset.DownloadUrl, stream, null, token).ConfigureAwait(false);
        stream.Position = 0;
        if (!await VerifySha256Async(stream, asset.Sha256, null, token).ConfigureAwait(false))
            throw new InvalidDataException("The extension update does not match its advertised hash.");

        stream.Position = 0;
        using var reader = new PackageArchiveReader(stream, leaveStreamOpen: true);
        if (!candidate.Equals(reader.GetIdentity()) || new LocalPackage(reader.NuspecReader).Tags.GetPackageKind() != PackageKind.Extension)
            throw new InvalidDataException("The download is not the requested extension.");
    }

    internal static StagedExtensionUpdate? FindStagedExtensionUpdate(PackageIdentity installed, NuGetVersion host, bool includeStandalone)
    {
        string directory = Path.Combine(ExtensionUpdateDirectory(installed), host.ToNormalizedString());
        string app = Path.Combine(directory, WithAppDirectory);
        string prefix = installed.Id + ".";
        StagedExtensionUpdate? latest = null;
        foreach (string channel in includeStandalone ? new[] { app, directory } : [app])
        {
            if (!Directory.Exists(channel)) continue;
            foreach (string path in Directory.EnumerateFileSystemEntries(channel))
            {
                string suffix = Path.GetExtension(path);
                // Earlier builds staged a single archive directly in the channel.
                if (suffix is not (".ready" or ".nupkg")) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && NuGetVersion.TryParse(name[prefix.Length..], out var version) && version > installed.Version
                    && (latest == null || version > latest.Package.Version)
                    && !File.Exists(Path.Combine(directory, name + ".rejected")))
                {
                    string feed = Path.Combine(channel, name + ".ready");
                    latest = new(new PackageIdentity(installed.Id, version), Directory.Exists(feed) ? feed : channel, channel == app);
                }
            }
        }
        return latest;
    }

    internal Task<bool> ResolveStagedExtensionAsync(StagedExtensionUpdate update, CancellationToken token)
    {
        var source = new PackageSource(update.Source);
        var context = new PackageInstallContext(update.Package.Id, update.Package.Version.ToNormalizedString(), string.Empty);
        return TrackAsync(() => ResolveDependenciesCoreAsync(context, false, null, token,
            _sourceRepositoryProvider.CreateRepository(source)));
    }

    internal void RejectStagedExtensionUpdate(PackageIdentity installed, PackageIdentity candidate, NuGetVersion host)
    {
        string directory = Path.Combine(ExtensionUpdateDirectory(installed), host.ToNormalizedString());
        string name = $"{installed.Id}.{candidate.Version.ToNormalizedString()}";
        File.WriteAllText(Path.Combine(directory, name + ".rejected"), string.Empty);
        foreach (string channel in new[] { directory, Path.Combine(directory, WithAppDirectory) })
        {
            if (!Directory.Exists(channel)) continue;
            DeleteIfExists(Path.Combine(channel, name + ".ready"));
            File.Delete(Path.Combine(channel, name + ".nupkg"));
        }
    }

    internal void ClearStagedExtensionUpdates(PackageIdentity installed)
        => DeleteIfExists(ExtensionUpdateDirectory(installed));
}
