using Beutl.Api.Clients;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public enum PackageInstallPhase
{
    Downloading = 0,
    Downloaded = 1,
    Verifying = 2,
    Verified = 3,
    ResolvingDependencies = 4,
    ResolvedDependencies = 5
}

public class PackageInstallContext(string packageName, string version, string downloadUrl)
{
    private PackageInstallPhase _phase;
    private IReadOnlyList<string>? _installedPaths;

    public string PackageName { get; } = packageName;

    public string Version { get; } = version;

    public string DownloadUrl { get; } = downloadUrl;

    public string? NuGetPackageFile { get; internal set; }

    public bool HashVerified { get; internal set; }

    public PackageInstallPhase Phase
    {
        get => _phase;
        internal set
        {
            if ((int)_phase > (int)value)
            {
                throw new Exception("It is not possible to go back before the current phase.");
            }

            _phase = value;
        }
    }

    public IReadOnlyList<string> InstalledPaths
    {
        get => _installedPaths ?? throw new InvalidOperationException("ResolvedDependencies <= Phase");
        internal set => _installedPaths = value;
    }

    public IList<(PackageIdentity, LicenseMetadata)> LicensesRequiringApproval { get; } = new List<(PackageIdentity, LicenseMetadata)>();

    internal FileResponse? Asset { get; set; }

    internal string? ApprovedAnalyticsManifestSha256 { get; set; }

    internal AnalyticsFeatureManifest? AnalyticsManifest { get; set; }

    internal string? MarketplacePackageId { get; set; }

    internal PackageAnalyticsProvenance? PersistVerifiedAnalyticsArtifact(string installedPath)
    {
        if (!HashVerified
            || AnalyticsManifest is null
            || Asset is not { Sha256: { } packageSha256 }
            || PackageAnalyticsProvenance.CreateVerified(
                MarketplacePackageId,
                packageSha256,
                AnalyticsManifest.Sha256) is not { } provenance
            || PackageAnalyticsProvenance.CanonicalizePackageId(PackageName)
                != provenance.CanonicalMarketplacePackageId
            || string.IsNullOrWhiteSpace(NuGetPackageFile)
            || !File.Exists(NuGetPackageFile)
            || !Directory.Exists(installedPath))
        {
            return null;
        }

        string destination = PackageAnalyticsProvenance.GetArtifactPath(installedPath);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            string actualSourceHash;
            using (FileStream source = File.OpenRead(NuGetPackageFile))
            {
                actualSourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
            }

            if (!StringComparer.OrdinalIgnoreCase.Equals(actualSourceHash, provenance.PackageSha256))
            {
                return null;
            }

            if (AnalyticsFeatureManifest.TryLoadFromPackageFile(
                    NuGetPackageFile,
                    provenance.ApprovedManifestSha256) is null)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(NuGetPackageFile, temporary, overwrite: false);

            string persistedHash;
            using (FileStream persisted = File.OpenRead(temporary))
            {
                persistedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(persisted));
            }

            if (!StringComparer.OrdinalIgnoreCase.Equals(persistedHash, provenance.PackageSha256))
            {
                return null;
            }

            File.Move(temporary, destination, overwrite: true);
            return provenance;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A failed cleanup must not turn installation into a telemetry dependency.
            }
        }
    }
}
