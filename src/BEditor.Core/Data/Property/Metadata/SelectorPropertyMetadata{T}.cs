using System.Collections.Generic;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="SelectorProperty{T}"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="ItemSource">The source of the item to be selected.</param>
    /// <param name="DefaultItem">The default value for <see cref="SelectorProperty{T}.SelectItem"/>.</param>
    /// <param name="MemberPath">The path to the member to display.</param>
    public record SelectorPropertyMetadata<T>(string Name, IList<T> ItemSource, T? DefaultItem = default, string MemberPath = "")
        : PropertyElementMetadata(Name), IPropertyBuilder<SelectorProperty<T>>
        where T : IJsonObject
    {
        /// <inheritdoc/>
        public SelectorProperty<T> Build()
        {
            return new(this);
        }
    }
}
