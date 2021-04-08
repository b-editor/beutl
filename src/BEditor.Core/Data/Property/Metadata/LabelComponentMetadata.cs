namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="LabelComponent"/>.
    /// </summary>
    public record LabelComponentMetadata()
        : PropertyElementMetadata(string.Empty), IEditingPropertyInitializer<LabelComponent>
    {
        /// <inheritdoc/>
        public LabelComponent Create()
        {
            return new();
        }
    }
}
