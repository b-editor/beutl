// Package.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the package of the plugin.
    /// </summary>
    public sealed class Package : IEquatable<Package?>
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

        /// <summary>
        /// Determines whether two specified <see cref="Package"/> objects are equal.
        /// </summary>
        /// <param name="left">The first <see cref="Package"/> object.</param>
        /// <param name="right">The second <see cref="Package"/> object.</param>
        /// <returns>true if v1 equals v2; otherwise, false.</returns>
        public static bool operator ==(Package? left, Package? right)
        {
            return EqualityComparer<Package>.Default.Equals(left, right);
        }

        /// <summary>
        /// Determines whether two specified <see cref="Package"/> objects are not equal.
        /// </summary>
        /// <param name="left">The first <see cref="Package"/> object.</param>
        /// <param name="right">The second <see cref="Package"/> object.</param>
        /// <returns>true if v1 does not equal v2; otherwise, false.</returns>
        public static bool operator !=(Package? left, Package? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as Package);
        }

        /// <inheritdoc/>
        public bool Equals(Package? other)
        {
            return other != null &&
                   MainAssembly == other.MainAssembly &&
                   Name == other.Name &&
                   Author == other.Author &&
                   HomePage == other.HomePage &&
                   DescriptionShort == other.DescriptionShort &&
                   Description == other.Description &&
                   Tag == other.Tag &&
                   Id.Equals(other.Id) &&
                   License == other.License;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            hash.Add(MainAssembly);
            hash.Add(Name);
            hash.Add(Author);
            hash.Add(HomePage);
            hash.Add(DescriptionShort);
            hash.Add(Description);
            hash.Add(Tag);
            hash.Add(Id);
            hash.Add(License);
            hash.Add(Versions);
            return hash.ToHashCode();
        }
    }
}