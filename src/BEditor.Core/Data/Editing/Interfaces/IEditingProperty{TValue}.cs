namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public interface IEditingProperty<TValue> : IEditingProperty
    {
        IEditingPropertyInitializer? IEditingProperty.Initializer => Initializer;

        IEditingPropertySerializer? IEditingProperty.Serializer
        {
            get => Serializer;
            init => Serializer = (IEditingPropertySerializer<TValue>?)value;
        }

        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer{TValue}"/> that initializes the local value of this <see cref="IEditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertyInitializer<TValue>? Initializer { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer{TValue}"/> that serializes the local value of this <see cref="IEditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertySerializer<TValue>? Serializer { get; init; }
    }
}