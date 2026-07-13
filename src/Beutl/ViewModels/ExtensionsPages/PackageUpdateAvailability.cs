using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.ViewModels.ExtensionsPages;

internal static class PackageUpdateAvailability
{
    public static bool IsAvailable(string? releaseVersion, PackageIdentity? installedPackage)
    {
        return installedPackage is not null
            && NuGetVersion.TryParse(releaseVersion, out NuGetVersion? parsedVersion)
            && parsedVersion > installedPackage.Version;
    }
}
