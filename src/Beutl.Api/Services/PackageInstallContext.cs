using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public enum PackageInstallPhase
{
    Downloading = 0,
    Downloaded = 1,
    ResolvingDependencies = 2,
    ResolvedDependencies = 3
}

public class PackageInstallContext
{
    private PackageInstallPhase _phase;
    private IReadOnlyList<string>? _installedPaths;

    public PackageInstallContext(Package package, Release release, Asset asset)
    {
        Package = package;
        Release = release;
        Asset = asset;
    }

    public Package Package { get; }

    public Release Release { get; }

    public Asset Asset { get; }

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
}
