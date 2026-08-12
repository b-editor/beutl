using System.Text.RegularExpressions;

namespace Beutl.Api.Services;

internal enum PackageAnalyticsSource
{
    Unknown,
    LocalSource,
    MarketplaceCandidate,
    VerifiedMarketplace
}

/// <summary>
/// Persisted trust evidence for a package. Exact telemetry feature IDs are allowed
/// only for a verified Marketplace package whose archived payload remains intact.
/// </summary>
internal sealed record PackageAnalyticsProvenance(
    PackageAnalyticsSource Source,
    string? CanonicalMarketplacePackageId = null,
    string? PackageSha256 = null,
    string? ApprovedManifestSha256 = null)
{
    internal const string ArtifactRelativePath = "beutl/marketplace-artifact.nupkg";

    private static readonly Regex s_canonicalPackageIdPattern = new(
        "^[a-z0-9](?:[a-z0-9.-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal bool IsVerified => Source == PackageAnalyticsSource.VerifiedMarketplace
        && IsCanonicalPackageId(CanonicalMarketplacePackageId)
        && IsSha256(PackageSha256)
        && IsSha256(ApprovedManifestSha256);

    internal static PackageAnalyticsProvenance Unknown { get; } = new(PackageAnalyticsSource.Unknown);

    internal static PackageAnalyticsProvenance? CreateVerified(
        string? marketplacePackageId,
        string? packageSha256,
        string? manifestSha256)
    {
        string? canonicalPackageId = CanonicalizePackageId(marketplacePackageId);
        return canonicalPackageId is not null
            && IsSha256(packageSha256)
            && IsSha256(manifestSha256)
            ? new PackageAnalyticsProvenance(
                PackageAnalyticsSource.VerifiedMarketplace,
                canonicalPackageId,
                packageSha256!.ToUpperInvariant(),
                manifestSha256!.ToUpperInvariant())
            : null;
    }

    internal static string GetArtifactPath(string installedPath)
    {
        return Path.Combine(installedPath, "beutl", "marketplace-artifact.nupkg");
    }

    internal static string? CanonicalizePackageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string canonical = value.ToLowerInvariant();
        return IsCanonicalPackageId(canonical) ? canonical : null;
    }

    internal static bool IsCanonicalPackageId(string? value)
    {
        return value is not null && s_canonicalPackageIdPattern.IsMatch(value);
    }

    internal static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}
