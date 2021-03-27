namespace BEditor.Data
{
    /// <summary>
    /// Represents the ability to create an instance of a local value of <see cref="EditingProperty{TValue}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the local value.</typeparam>
    public interface IPropertyBuilder<T> : IPropertyBuilder
    {
        /// <inheritdoc/>
        object IPropertyBuilder.Build()
        {
            return Build()!;
        }

        /// <summary>
        /// Create a local value instance of <see cref="EditingProperty{TValue}"/>.
        /// </summary>
        /// <returns>Returns the created local value.</returns>
        public new T Build();
    }
}
