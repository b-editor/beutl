using System.Diagnostics.CodeAnalysis;

using Beutl.Api.Objects;

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

public class PackageInstallContext
{
    private PackageInstallPhase _phase;
    private IReadOnlyList<string>? _installedPaths;

    public PackageInstallContext(string packageName, string version, string downloadUrl)
    {
        PackageName = packageName;
        Version = version;
        DownloadUrl = downloadUrl;
    }

    public string PackageName { get; }

    public string Version { get; }

    public string DownloadUrl { get; }

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

    internal Asset? Asset { get; set; }
}
