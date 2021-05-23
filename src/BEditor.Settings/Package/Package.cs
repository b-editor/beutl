using System;
using System.Text.Json.Serialization;

namespace BEditor.Package
{
    public sealed class Package
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("homepage")]
        public string HomePage { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.Empty;

        [JsonPropertyName("versions")]
        public PackageVersion[] Versions { get; set; } = Array.Empty<PackageVersion>();
    }

    public class PackageVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("download")]
        public string Download { get; set; } = string.Empty;

        public Version ToVersion()
        {
            return new Version(Version);
        }
    }
}