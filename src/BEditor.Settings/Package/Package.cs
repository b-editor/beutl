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

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("download")]
        public string Download { get; set; } = string.Empty;

        [JsonPropertyName("versions")]
        public string[] Versions { get; set; } = Array.Empty<string>();
    }
}