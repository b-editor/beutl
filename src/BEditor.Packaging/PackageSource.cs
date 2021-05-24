using System;
using System.Text.Json.Serialization;

namespace BEditor.Packaging
{
    public sealed class PackageSource
    {
        [JsonPropertyName("packages")]
        public Package[] Packages { get; set; } = Array.Empty<Package>();

        [JsonIgnore]
        public PackageSourceInfo? Info { get; set; }
    }
}