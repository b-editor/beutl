// EditingPropertyInitializer{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the ability to create an instance of a local value of <see cref="EditingProperty{TValue}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the local value.</typeparam>
    public class EditingPropertyInitializer<T> : IEditingPropertyInitializer<T>
    {
        private readonly Func<T> _oncreate;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingPropertyInitializer{T}"/> class.
        /// </summary>
        /// <param name="create">Create a new instance.</param>
        public EditingPropertyInitializer(Func<T> create)
        {
            _oncreate = create;
        }

        /// <summary>
        /// Create a local value instance of <see cref="EditingProperty{TValue}"/>.
        /// </summary>
        /// <returns>Returns the created local value.</returns>
        public T Create()
        {
            return _oncreate();
        }
    }
}