namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="ButtonComponent"/>.
    /// <param name="Name">The string displayed in the property header.</param>
    /// </summary>
    public record ButtonComponentMetadata(string Name)
        : PropertyElementMetadata(Name), IPropertyBuilder<ButtonComponent>
    {
        /// <inheritdoc/>
        public ButtonComponent Build()
        {
            return new ButtonComponent(this);
        }
    }
}
