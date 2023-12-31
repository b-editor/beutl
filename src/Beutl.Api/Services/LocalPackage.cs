using System.Diagnostics.CodeAnalysis;

using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public class LocalPackage
{
    // LoadPrimitiveExtensionTask
    internal const int Reserved0 = 0;

    internal static int s_nextId = 2;

    public LocalPackage()
    {
        LocalId = Interlocked.Increment(ref s_nextId);
    }

    public LocalPackage(NuspecReader nuspecReader)
        : this()
    {
        Name = nuspecReader.GetId();
        DisplayName = nuspecReader.GetTitle();
        Version = nuspecReader.GetVersion().ToString();
        Publisher = nuspecReader.GetAuthors();
        WebSite = nuspecReader.GetProjectUrl();
        Description = nuspecReader.GetReleaseNotes();
        ShortDescription = nuspecReader.GetDescription();

        NuGetFramework framework = Helper.GetFrameworkName();
        IEnumerable<PackageDependencyGroup> depGroups = nuspecReader.GetDependencyGroups();
        NuGetFramework? nearest = Helper.FrameworkReducer.GetNearest(
            framework,
            depGroups.Select(v => v.TargetFramework));

        if (nearest != null)
        {
            PackageDependencyGroup depGroup = depGroups.First(v => v.TargetFramework == nearest);
            PackageDependency? sdkDep = depGroup.Packages.FirstOrDefault(v => v.Id == "Beutl.Sdk");
            if (sdkDep != null)
            {
                TargetVersion = sdkDep.VersionRange.ToShortString();
            }
        }

        //Logo = nuspecReader.GetIcon();
        Tags = [.. nuspecReader.GetTags().Split(' ', ';')];
    }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string WebSite { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ShortDescription { get; set; } = string.Empty;

    public string Logo { get; set; } = string.Empty;

    // VersionRange
    public string? TargetVersion { get; set; }

    public List<string> Tags { get; set; } = [];

    [AllowNull]
    public string InstalledPath { get; internal set; }

    public bool SideLoad { get; set; }

    public int LocalId { get; }
}
