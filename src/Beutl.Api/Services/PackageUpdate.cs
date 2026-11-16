using Beutl.Api.Objects;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public sealed class PackageUpdate(Package package, Release? oldVersion, Release newVersion)
{
    public Package Package { get; } = package;

    public Release? OldVersion { get; } = oldVersion;

    public Release NewVersion { get; } = newVersion;

    internal static bool IsAvailable(Release? release, NuGetVersion? installedVersion)
    {
        return installedVersion is not null && release?.CanInstall(installedVersion) == true;
    }
}
