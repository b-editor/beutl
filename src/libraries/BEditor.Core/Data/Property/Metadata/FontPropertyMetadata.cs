// FontPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Linq;

using BEditor.Drawing;
using BEditor.LangResources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="FontProperty"/>.
    /// </summary>
    public record FontPropertyMetadata : PropertyElementMetadata, IEditingPropertyInitializer<FontProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FontPropertyMetadata"/> class.
        /// </summary>
        public FontPropertyMetadata()
            : base(Strings.Font)
        {
            SelectItem = FontManager.Default.LoadedFonts.FirstOrDefault()!;
        }

        /// <summary>
        /// Gets the default selected item.
        /// </summary>
        public Font SelectItem { get; init; }

        /// <inheritdoc/>
        public FontProperty Create()
        {
            return new(this);
        }
    }
}