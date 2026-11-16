using Beutl.Api.Objects;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

internal sealed record StagedExtensionUpdate(PackageIdentity Package, string Source, bool WithAppUpdate);

public partial class PackageInstaller
{
    private const string WithAppDirectory = "app";

    // Both ends of an update are part of its location. A postponed Beutl update or a manually
    // replaced extension cannot apply a download meant for another version.
    private static string ExtensionUpdateDirectory(PackageIdentity installed)
        => Path.Combine(Helper.AppRoot, "extensionUpdates", ValidatePackageName(installed.Id).ToLowerInvariant(), installed.Version.ToNormalizedString());

    internal Task StageExtensionUpdateAsync(PackageIdentity installed, Release release, NuGetVersion host, bool appUpdate, CancellationToken token)
        => TrackAsync(async () =>
        {
            var candidate = new PackageIdentity(installed.Id, NuGetVersion.Parse(release.Version.Value));
            string directory = Path.Combine(ExtensionUpdateDirectory(installed), host.ToNormalizedString());
            if (appUpdate) directory = Path.Combine(directory, WithAppDirectory);
            string archive = Path.Combine(directory, $"{installed.Id}.{candidate.Version.ToNormalizedString()}.nupkg");
            if (File.Exists(archive)) return;

            var asset = await release.GetAssetAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(asset.Sha256))
                throw new InvalidDataException("The extension update has no SHA-256 digest.");

            Directory.CreateDirectory(directory);
            string temporary = archive + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    await Download(asset.DownloadUrl, stream, null, token).ConfigureAwait(false);
                    stream.Position = 0;
                    if (!await VerifySha256Async(stream, asset.Sha256, null, token).ConfigureAwait(false))
                        throw new InvalidDataException("The extension update does not match its advertised hash.");

                    stream.Position = 0;
                    using var reader = new PackageArchiveReader(stream, leaveStreamOpen: true);
                    if (!candidate.Equals(reader.GetIdentity())
                        || new LocalPackage(reader.NuspecReader).Tags.GetPackageKind() != PackageKind.Extension)
                        throw new InvalidDataException("The download is not the requested extension.");
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, archive, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        });

    internal static StagedExtensionUpdate? FindStagedExtensionUpdate(PackageIdentity installed, NuGetVersion host, bool includeStandalone)
    {
        string directory = Path.Combine(ExtensionUpdateDirectory(installed), host.ToNormalizedString());
        if (!includeStandalone) directory = Path.Combine(directory, WithAppDirectory);
        if (!Directory.Exists(directory)) return null;

        string prefix = installed.Id + ".";
        StagedExtensionUpdate? latest = null;
        foreach (string path in Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && NuGetVersion.TryParse(name[prefix.Length..], out NuGetVersion? version)
                && version > installed.Version && (latest == null || version > latest.Package.Version))
            {
                string source = Path.GetDirectoryName(path)!;
                latest = new(new PackageIdentity(installed.Id, version), source, Path.GetFileName(source) == WithAppDirectory);
            }
        }
        return latest;
    }

    internal Task ResolveStagedExtensionAsync(StagedExtensionUpdate update, CancellationToken token)
    {
        var source = new PackageSource(update.Source);
        var context = new PackageInstallContext(update.Package.Id, update.Package.Version.ToNormalizedString(), string.Empty);
        return TrackAsync(() => ResolveDependenciesCoreAsync(context, false, null, token,
            _sourceRepositoryProvider.CreateRepository(source)));
    }

    internal void ClearStagedExtensionUpdates(PackageIdentity installed)
        => DeleteIfExists(ExtensionUpdateDirectory(installed));
}
