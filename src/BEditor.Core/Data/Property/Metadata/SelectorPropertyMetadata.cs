using System.Collections;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="SelectorProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="ItemSource">The source of the item to be selected</param>
    /// <param name="DefaultIndex">The default value for <see cref="SelectorProperty.Index"/></param>
    /// <param name="MemberPath">The path to the member to display</param>
    public record SelectorPropertyMetadata(string Name, IList ItemSource, int DefaultIndex = 0, string MemberPath = "")
        : PropertyElementMetadata(Name), IPropertyBuilder<SelectorProperty>
    {
        /// <inheritdoc/>
        public SelectorProperty Build()
        {
            return new(this);
        }
    }
}
