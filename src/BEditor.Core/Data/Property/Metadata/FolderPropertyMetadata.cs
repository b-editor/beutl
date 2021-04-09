namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="FolderProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="Default">The default value of <see cref="FolderProperty.Value"/>.</param>
    public record FolderPropertyMetadata(string Name, string Default = "")
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<FolderProperty>
    {
        /// <inheritdoc/>
        public FolderProperty Create()
        {
            return new(this);
        }
    }
}
