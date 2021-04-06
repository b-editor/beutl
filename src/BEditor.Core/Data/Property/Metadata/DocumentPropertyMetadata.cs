namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="DocumentProperty"/>.
    /// </summary>
    /// <param name="DefaultText">The default value of <see cref="DocumentProperty.Value"/>.</param>
    public record DocumentPropertyMetadata(string DefaultText)
        : PropertyElementMetadata(string.Empty), IPropertyBuilder<DocumentProperty>
    {
        /// <inheritdoc/>
        public DocumentProperty Build()
        {
            return new(this);
        }
    }
}
