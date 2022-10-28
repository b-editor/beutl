using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class LocalPackage
{
    internal static int s_nextId;
    internal readonly int _id;

    public LocalPackage()
    {
        _id = s_nextId++;
    }

    public LocalPackage(Package package, Release release)
    {
        Name = package.Name;
        DisplayName = package.DisplayName.Value;
        Version = release.Version.Value;
        Publisher = package.Owner.Name;
        WebSite = package.WebSite.Value;
        Description = package.Description.Value;
        ShortDescription = package.ShortDescription.Value;
        Tags = package.Tags.Value.ToList();
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;

    [JsonPropertyName("website")]
    public string WebSite { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("short_description")]
    public string ShortDescription { get; set; } = string.Empty;

    [JsonPropertyName("assembly")]
    public string Assembly { get; set; } = string.Empty;
    
    [JsonPropertyName("logo")]
    public string Logo { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new List<string>();

    [JsonIgnore]
    public string? BasePath { get; set; }

    [JsonIgnore]
    public int LocalId => _id;
}
