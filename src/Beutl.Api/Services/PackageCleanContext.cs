using System.Diagnostics.CodeAnalysis;

namespace Beutl.Api.Services;

public class PackageCleanContext
{
    public PackageCleanContext(string[] unnecessaryPackages, long sizeToBeReleased)
    {
        UnnecessaryPackages = unnecessaryPackages;
        SizeToBeReleased = sizeToBeReleased;
    }

    public string[] UnnecessaryPackages { get; }

    public long SizeToBeReleased { get; }

    [AllowNull]
    public IReadOnlyList<string> FailedPackages { get; internal set; }
}
