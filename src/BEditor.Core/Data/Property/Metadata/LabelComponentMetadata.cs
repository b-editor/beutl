namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="LabelComponent"/>.
    /// </summary>
    public record LabelComponentMetadata()
        : PropertyElementMetadata(string.Empty), IPropertyBuilder<LabelComponent>
    {
        /// <inheritdoc/>
        public LabelComponent Build()
        {
            return new();
        }
    }
}
