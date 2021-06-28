// EditingProperty{TValue}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        /// <param name="owner">The owner type.</param>
        public EditingProperty(string name, Type owner)
            : base(name, typeof(TValue), owner)
        {
        }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer{TValue}"/> that serializes the local value of this <see cref="EditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertySerializer<TValue>? Serializer
        {
            get => base.Serializer as IEditingPropertySerializer<TValue>;
            init => base.Serializer = value;
        }

        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer{TValue}"/> that initializes the local value of this <see cref="EditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertyInitializer<TValue>? Initializer
        {
            get => base.Initializer as IEditingPropertyInitializer<TValue>;
            init => base.Initializer = value;
        }
    }
}