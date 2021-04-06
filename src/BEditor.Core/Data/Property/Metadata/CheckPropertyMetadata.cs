namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="CheckProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultIsChecked">The default value for <see cref="CheckProperty.Value"/>.</param>
    public record CheckPropertyMetadata(string Name, bool DefaultIsChecked = false)
        : PropertyElementMetadata(Name), IPropertyBuilder<CheckProperty>
    {
        /// <inheritdoc/>
        public CheckProperty Build()
        {
            return new(this);
        }
    }
}
