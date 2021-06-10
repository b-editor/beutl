// PackageVersion.cs
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
    }
}