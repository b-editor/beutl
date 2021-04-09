namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="ButtonComponent"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record ButtonComponentMetadata(string Name)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<ButtonComponent>
    {
        /// <inheritdoc/>
        public ButtonComponent Create()
        {
            return new ButtonComponent(this);
        }
    }
}
