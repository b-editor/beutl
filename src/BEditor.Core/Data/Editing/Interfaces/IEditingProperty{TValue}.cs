// IEditingProperty{TValue}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public interface IEditingProperty<TValue> : IEditingProperty
    {
        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer{TValue}"/> that initializes the local value of this <see cref="IEditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertyInitializer<TValue>? Initializer { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer{TValue}"/> that serializes the local value of this <see cref="IEditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertySerializer<TValue>? Serializer { get; init; }

        /// <inheritdoc/>
        IEditingPropertyInitializer? IEditingProperty.Initializer => Initializer;

        /// <inheritdoc/>
        IEditingPropertySerializer? IEditingProperty.Serializer
        {
            get => Serializer;
            init => Serializer = (IEditingPropertySerializer<TValue>?)value;
        }
    }
}