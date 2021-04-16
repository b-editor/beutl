using System;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public class EditingProperty<TValue> : EditingProperty, IEditingProperty<TValue>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty{TValue}"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="owner">The type of the owner.</param>
        public EditingProperty(string name, Type owner) : base(name, owner, typeof(TValue))
        {
        }

        /// <inheritdoc/>
        public new IEditingPropertySerializer<TValue>? Serializer
        {
            get => base.Serializer as IEditingPropertySerializer<TValue>;
            init => base.Serializer = value;
        }

        /// <inheritdoc/>
        public new IEditingPropertyInitializer<TValue>? Initializer
        {
            get => base.Initializer as IEditingPropertyInitializer<TValue>;
            init => base.Initializer = value;
        }
    }
}