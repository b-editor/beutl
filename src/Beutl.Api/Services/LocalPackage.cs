using System.Diagnostics.CodeAnalysis;

using Beutl.Api.Objects;

using NuGet.Packaging;

namespace Beutl.Api.Services;

public class LocalPackage
{
    internal static int s_nextId;

    public LocalPackage()
    {
        LocalId = s_nextId++;
    }

    public LocalPackage(Package package)
        : this()
    {
        Name = package.Name;
        DisplayName = package.DisplayName.Value;
        Publisher = package.Owner.Name;
        WebSite = package.WebSite.Value;
        Description = package.Description.Value;
        ShortDescription = package.ShortDescription.Value;
        Tags = package.Tags.Value.ToList();
    }

    public LocalPackage(Package package, Release release)
        : this(package)
    {
        Version = release.Version.Value;
    }

    public LocalPackage(NuspecReader nuspecReader)
    {
        Name = nuspecReader.GetId();
        DisplayName = nuspecReader.GetTitle();
        Version = nuspecReader.GetVersion().ToString();
        Publisher = nuspecReader.GetAuthors();
        WebSite = nuspecReader.GetProjectUrl();
        Description = nuspecReader.GetReadme();
        ShortDescription = nuspecReader.GetDescription();
        //Logo = nuspecReader.GetIcon();
        Tags = nuspecReader.GetTags().Split(' ', ';').ToList();

        LocalId = -1;
    }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string WebSite { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ShortDescription { get; set; } = string.Empty;

    public string Logo { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new List<string>();

    [AllowNull]
    public string InstalledPath { get; internal set; }

    public int LocalId { get; }
}
