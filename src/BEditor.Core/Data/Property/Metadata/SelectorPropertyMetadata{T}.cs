using System;
using System.Collections.Generic;
using System.Linq;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="SelectorProperty{T}"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="ItemSource">The source of the item to be selected.</param>
    /// <param name="Selector">A function to get a string from an item.</param>
    /// <param name="DefaultItem">The default value for <see cref="SelectorProperty{T}.SelectItem"/>.</param>
    public record SelectorPropertyMetadata<T>(string Name, IList<T> ItemSource, Func<T, string> Selector, T? DefaultItem = default)
        : PropertyElementMetadata(Name), IPropertyBuilder<SelectorProperty<T>>
        where T : IJsonObject, IEquatable<T>
    {
        private IEnumerable<string>? _displayStrings;

        /// <summary>
        /// Gets the strings to display in the UI.
        /// </summary>
        public IEnumerable<string> DisplayStrings => _displayStrings ??= ItemSource.Select(Selector);

        /// <inheritdoc/>
        public SelectorProperty<T> Build()
        {
            return new(this);
        }
    }
}
