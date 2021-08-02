// PackageVersion.cs
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
    /// Indicates the version of the package.
    /// </summary>
    public class PackageVersion : IEquatable<PackageVersion?>
    {
        /// <summary>
        /// Gets or sets the version of the package.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the download url of the package.
        /// </summary>
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the update note.
        /// </summary>
        [JsonPropertyName("update_note")]
        public string UpdateNote { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the short update note.
        /// </summary>
        [JsonPropertyName("update_note_short")]
        public string UpdateNoteShort { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the date time of the release.
        /// </summary>
        [JsonPropertyName("release_datetime")]
        public DateTime ReleaseDateTime { get; set; }

        /// <summary>
        /// Determines whether two specified <see cref="PackageVersion"/> objects are equal.
        /// </summary>
        /// <param name="left">The first <see cref="PackageVersion"/> object.</param>
        /// <param name="right">The second <see cref="PackageVersion"/> object.</param>
        /// <returns>true if v1 equals v2; otherwise, false.</returns>
        public static bool operator ==(PackageVersion? left, PackageVersion? right)
        {
            return EqualityComparer<PackageVersion>.Default.Equals(left, right);
        }

        /// <summary>
        /// Determines whether two specified <see cref="PackageVersion"/> objects are not equal.
        /// </summary>
        /// <param name="left">The first <see cref="PackageVersion"/> object.</param>
        /// <param name="right">The second <see cref="PackageVersion"/> object.</param>
        /// <returns>true if v1 does not equal v2; otherwise, false.</returns>
        public static bool operator !=(PackageVersion? left, PackageVersion? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as PackageVersion);
        }

        /// <inheritdoc/>
        public bool Equals(PackageVersion? other)
        {
            return other != null &&
                   Version == other.Version &&
                   DownloadUrl == other.DownloadUrl &&
                   UpdateNote == other.UpdateNote &&
                   UpdateNoteShort == other.UpdateNoteShort &&
                   ReleaseDateTime == other.ReleaseDateTime;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Version, DownloadUrl, UpdateNote, UpdateNoteShort, ReleaseDateTime);
        }
    }
}