namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="DocumentProperty"/>.
    /// </summary>
    /// <param name="DefaultText">The default value of <see cref="DocumentProperty.Value"/>.</param>
    public record DocumentPropertyMetadata(string DefaultText)
        : PropertyElementMetadata(string.Empty), IEditingPropertyInitializer<DocumentProperty>
    {
        /// <inheritdoc/>
        public DocumentProperty Create()
        {
            return new(this);
        }
    }
}
