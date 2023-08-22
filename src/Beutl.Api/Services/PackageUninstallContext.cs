using System.Diagnostics.CodeAnalysis;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class PackageUninstallContext
{
    public PackageUninstallContext(PackageIdentity packageIdentity, string? installedPath = null)
    {
        Id = packageIdentity;
        PackageId = Id.Id;
        Version = Id.Version.ToString();
        InstalledPath = installedPath ?? Helper.PackagePathResolver.GetInstalledPath(packageIdentity);
    }

    public PackageIdentity Id { get; }

    [AllowNull]
    internal PackageIdentity[] UnnecessaryPackages { get; init; }

    //internal string[] UnnecessaryPackages { get; require init; }

    public string InstalledPath { get; }

    public string PackageId { get; }

    public string Version { get; }

    public long SizeToBeReleased { get; init; }

    [AllowNull]
    public IReadOnlyList<string> FailedPackages { get; internal set; }
}
