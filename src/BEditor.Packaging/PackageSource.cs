// PackageSource.cs
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
    /// Represents the package source.
    /// </summary>
    public sealed class PackageSource
    {
        /// <summary>
        /// Gets or sets the packages included in this package source.
        /// </summary>
        [JsonPropertyName("packages")]
        public Package[] Packages { get; set; } = Array.Empty<Package>();

        /// <summary>
        /// Gets or sets the package source infomation.
        /// </summary>
        [JsonIgnore]
        public PackageSourceInfo? Info { get; set; }
    }
}