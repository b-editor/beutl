// ContainerMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Media
{
    /// <summary>
    /// Represents multimedia file metadata info.
    /// </summary>
    public class ContainerMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerMetadata"/> class.
        /// </summary>
        public ContainerMetadata()
        {
        }

        /// <summary>
        /// Gets or sets the multimedia title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia author info.
        /// </summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia album name.
        /// </summary>
        public string Album { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets multimedia release date/year.
        /// </summary>
        public string Year { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia genre.
        /// </summary>
        public string Genre { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia language.
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia copyright info.
        /// </summary>
        public string Copyright { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia rating.
        /// </summary>
        public string Rating { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the multimedia track number string.
        /// </summary>
        public string TrackNumber { get; set; } = string.Empty;
    }
}