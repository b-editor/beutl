using System.Diagnostics.CodeAnalysis;

using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public class PackageCleanContext(PackageIdentity[] unnecessaryPackages, long sizeToBeReleased)
{
    public PackageIdentity[] UnnecessaryPackages { get; } = unnecessaryPackages;

    public long SizeToBeReleased { get; } = sizeToBeReleased;

    [AllowNull]
    public IReadOnlyList<string> FailedPackages { get; internal set; }
}
