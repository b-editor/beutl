namespace BEditor.Data
{
    /// <summary>
    /// Represents the ability to create an instance of a local value of <see cref="EditingProperty"/>.
    /// </summary>
    public interface IPropertyBuilder
    {
        /// <summary>
        /// Create a local value instance of <see cref="EditingProperty"/>.
        /// </summary>
        /// <returns>Returns the created local value.</returns>
        public object Build();
    }
}
