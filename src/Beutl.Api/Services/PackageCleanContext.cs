using System.Diagnostics.CodeAnalysis;

using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public class PackageCleanContext
{
    public PackageCleanContext(PackageIdentity[] unnecessaryPackages, long sizeToBeReleased)
    {
        UnnecessaryPackages = unnecessaryPackages;
        SizeToBeReleased = sizeToBeReleased;
    }

    public PackageIdentity[] UnnecessaryPackages { get; }

    public long SizeToBeReleased { get; }

    [AllowNull]
    public IReadOnlyList<string> FailedPackages { get; internal set; }
}
