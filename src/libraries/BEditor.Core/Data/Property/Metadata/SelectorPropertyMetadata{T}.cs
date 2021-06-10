// SelectorPropertyMetadata{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="SelectorProperty{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of element.</typeparam>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="ItemSource">The source of the item to be selected.</param>
    /// <param name="Selector">A function to get a string from an item.</param>
    /// <param name="DefaultIndex">The default value for <see cref="SelectorProperty{T}.Index"/>.</param>
    public record SelectorPropertyMetadata<T>(string Name, IList<T> ItemSource, Func<T, string> Selector, int DefaultIndex = 0)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<SelectorProperty<T>>
        where T : IJsonObject, IEquatable<T>
    {
        private IEnumerable<string>? _displayStrings;

        /// <summary>
        /// Gets the strings to display in the UI.
        /// </summary>
        public IEnumerable<string> DisplayStrings => _displayStrings ??= ItemSource.Select(Selector);

        /// <inheritdoc/>
        public SelectorProperty<T> Create()
        {
            return new(this);
        }
    }
}