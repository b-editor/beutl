// PackageVersion.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Text.Json.Serialization;

namespace BEditor.Packaging
{
    /// <summary>
    /// Indicates the version of the package.
    /// </summary>
    public class PackageVersion
    {
        /// <summary>
        /// Gets or sets the version of the package.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the download url of the package.
        /// </summary>
        [JsonPropertyName("download")]
        public string Download { get; set; } = string.Empty;
    }
}