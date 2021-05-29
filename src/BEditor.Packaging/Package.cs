// Package.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Text.Json.Serialization;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the package of the plugin.
    /// </summary>
    public sealed class Package
    {
        /// <summary>
        /// Gets or sets the assembly name of the plugin.
        /// </summary>
        [JsonPropertyName("main_assembly")]
        public string MainAssembly { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the package.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the author of the package.
        /// </summary>
        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the url of website.
        /// </summary>
        [JsonPropertyName("homepage")]
        public string HomePage { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the short description of the package.
        /// </summary>
        [JsonPropertyName("description_short")]
        public string DescriptionShort { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description of the package.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the tag of the package.
        /// </summary>
        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the id of the package.
        /// </summary>
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.Empty;

        /// <summary>
        /// Gets or sets the license of the package.
        /// </summary>
        [JsonPropertyName("license")]
        public string License { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the all versions of the package.
        /// </summary>
        [JsonPropertyName("versions")]
        public PackageVersion[] Versions { get; set; } = Array.Empty<PackageVersion>();
    }
}