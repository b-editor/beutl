// SelectorPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="SelectorProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="ItemSource">The source of the item to be selected.</param>
    /// <param name="DefaultIndex">The default value for <see cref="SelectorProperty.Index"/>.</param>
    public record SelectorPropertyMetadata(string Name, IList<string> ItemSource, int DefaultIndex = 0)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<SelectorProperty>
    {
        /// <inheritdoc/>
        public SelectorProperty Create()
        {
            return new(this);
        }
    }
}