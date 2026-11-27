using System.Security.Cryptography;
using Beutl.Api.Objects;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

internal sealed record ExtensionInstallArchive(PackageIdentity Identity, string Path);

internal sealed class PackageLicenseRequiredException(PackageIdentity package)
    : InvalidOperationException($"The license for '{package.Id}' {package.Version} must be accepted in Beutl.")
{
    public PackageIdentity Package { get; } = package;
}

internal sealed class PackageTargetMismatchException(string message) : InvalidOperationException(message);

internal sealed class PackageArchiveConflictException(PackageIdentity package)
    : InvalidOperationException($"The cached archive for '{package.Id}' {package.Version} differs from the requested archive.");

public partial class PackageInstaller
{
    private static string PackageFileChangesLockPath => Path.Combine(Helper.AppRoot, ".package-file-changes.lock");

    // Avoid pruning the extracted files while the extension is still being activated and registered.
    internal static Task<FileStream> LockExtensionInstallAsync(CancellationToken cancellationToken)
        => PackageFileLock.AcquireAsync(PackageFileChangesLockPath, cancellationToken);

    internal Task<ExtensionInstallArchive> PrepareExtensionArchiveAsync(
        string directory, Release? release, string? path, Uri? url, CancellationToken cancellationToken)
        => TrackAsync(async () =>
        {
            string temporaryPath = Path.Combine(directory, "download.nupkg");
            if (release is not null)
            {
                var identity = new PackageIdentity(release.Package.Name, NuGetVersion.Parse(release.Version.Value));
                await DownloadExtensionArchiveAsync(identity, release, temporaryPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (path is not null)
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Download(url!.AbsoluteUri, output, null, cancellationToken, authorizeApiOrigin: false).ConfigureAwait(false);
                }
            }

            PackageIdentity package;
            using (var reader = new PackageArchiveReader(temporaryPath))
            {
                package = reader.GetIdentity();
                ValidatePackageName(package.Id);
                if (!PackageIdValidator.IsValidPackageId(package.Id))
                    throw new InvalidDataException("The archive does not declare a valid NuGet package ID.");
                var local = new LocalPackage(reader.NuspecReader);
                if (local.Tags.GetPackageKind() != PackageKind.Extension)
                    throw new InvalidDataException("The archive must be a Beutl extension package.");
                if (!PackageCompatibility.SupportsCurrentBeutlVersion(local.TargetVersion))
                    throw new PackageTargetMismatchException($"'{package.Id}' {package.Version} requires Beutl {local.TargetVersion}.");
                RequireAcceptedLicense(package, reader.NuspecReader);
            }
            cancellationToken.ThrowIfCancellationRequested();

            // Resolve this archive as a flat, private NuGet source with no competing copy of the root package.
            string archivePath = Path.Combine(directory, StagedName(package.Id, package.Version) + ".nupkg");
            File.Move(temporaryPath, archivePath);
            return new ExtensionInstallArchive(package, archivePath);
        });

    internal Task ResolveExtensionInstallAsync(ExtensionInstallArchive archive, CancellationToken cancellationToken)
    {
        var context = new PackageInstallContext(archive.Identity.Id, archive.Identity.Version.ToString(), "")
        {
            Phase = PackageInstallPhase.Downloaded
        };
        return TrackAsync(() => ResolveDependenciesCoreAsync(context, useSavedSelection: false, logger: null,
            cancellationToken, selectedArchive: archive));
    }

    private void RequireAcceptedLicense(PackageIdentity package, NuspecReader nuspec)
    {
        if (nuspec.GetRequireLicenseAcceptance()
            && !_apiApplication.GetResource<AcceptedLicenseManager>().Accepted.ContainsKey(package))
            throw new PackageLicenseRequiredException(package);
    }

    private static async Task InstallSelectedArchiveAsync(
        ExtensionInstallArchive archive, PackageExtractionContext extractionContext, CancellationToken cancellationToken)
    {
        using var lease = await LockPackageExtractionAsync(cancellationToken).ConfigureAwait(false);
        if (Helper.PackagePathResolver.GetInstalledPath(archive.Identity) is null)
        {
            using var input = File.OpenRead(archive.Path);
            await PackageExtractor.ExtractPackageAsync(Path.GetDirectoryName(archive.Path)!, input,
                Helper.PackagePathResolver, extractionContext, cancellationToken).ConfigureAwait(false);
        }
        await VerifyExtensionArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task VerifyExtensionArchiveAsync(ExtensionInstallArchive archive, CancellationToken cancellationToken)
    {
        string? installedPath = Helper.PackagePathResolver.GetInstalledPackageFilePath(archive.Identity);
        if (installedPath is null)
            throw new InvalidDataException($"The archive for '{archive.Identity}' is missing from the install directory.");
        using var selected = File.OpenRead(archive.Path);
        using var installed = File.OpenRead(installedPath);
        byte[] expected = await SHA256.HashDataAsync(selected, cancellationToken).ConfigureAwait(false);
        if (selected.Length != installed.Length ||
            !await VerifySha256Async(installed, Convert.ToHexString(expected), null, cancellationToken).ConfigureAwait(false))
            throw new PackageArchiveConflictException(archive.Identity);
    }
}
