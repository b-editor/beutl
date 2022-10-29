using System.Diagnostics.CodeAnalysis;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class PackageUninstallContext
{
    public PackageUninstallContext(string packageId, string version)
    {
        PackageId = packageId;
        Version = version;

        Id = new PackageIdentity(packageId, NuGetVersion.Parse(version));
        InstalledPath = Helper.PackagePathResolver.GetInstalledPath(Id);
    }

    public PackageUninstallContext(string installedPath)
    {
        var reader = new PackageFolderReader(installedPath);

        Id = reader.NuspecReader.GetIdentity();
        PackageId = Id.Id;
        Version = Id.Version.ToString();
        InstalledPath = installedPath;
    }

    internal PackageIdentity Id { get; }

    [AllowNull]
    internal string[] UnnecessaryPackages { get; init; }

    //internal string[] UnnecessaryPackages { get; require init; }

    public string InstalledPath { get; }

    public string PackageId { get; }

    public string Version { get; }

    public long SizeToBeReleased { get; init; }

    [AllowNull]
    public IReadOnlyList<string> FailedPackages { get; internal set; }
}
