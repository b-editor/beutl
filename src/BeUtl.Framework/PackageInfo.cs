using System.Text.Json.Serialization;

namespace BeUtl.Framework;

public class PackageInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public Version Version { get; set; } = new Version();

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

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new List<string>();

    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("license")]
    public PackageLicense License { get; set; }

    [JsonIgnore]
    public string? BasePath { get; set; }
}
