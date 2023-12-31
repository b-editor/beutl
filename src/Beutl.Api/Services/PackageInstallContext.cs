using System.Diagnostics.CodeAnalysis;

using Beutl.Api.Objects;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Licenses;

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

    internal Asset? Asset { get; set; }
}
