using System.Diagnostics.CodeAnalysis;

using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public class PackageCleanContext(PackageIdentity[] unnecessaryPackages, long sizeToBeReleased)
{
    public PackageIdentity[] UnnecessaryPackages { get; } = unnecessaryPackages;

    public long SizeToBeReleased { get; } = sizeToBeReleased;

    /// <summary>
    /// True when cleaning was skipped because the installed-package registry could not be restored.
    /// </summary>
    public bool RegistryRestoreFailed { get; init; }

    [AllowNull]
    public IReadOnlyList<string> FailedPackages { get; internal set; }
}
