// IEditingProperty.cs
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
    public interface IEditingProperty
    {
        /// <summary>
        /// Get the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.
        /// </summary>
        public bool IsDisposable { get; }

        /// <summary>
        /// Gets whether to be notified of property changes.
        /// </summary>
        public bool NotifyPropertyChanged { get; }

        /// <summary>
        /// Gets the name of this <see cref="IEditingProperty"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the owner type of this <see cref="IEditingProperty"/>.
        /// </summary>
        public Type OwnerType { get; }

        /// <summary>
        /// Gets the value type of this <see cref="IEditingProperty"/>.
        /// </summary>
        public Type ValueType { get; }

        /// <summary>
        /// Gets the Id.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer"/> that initializes the local value of this <see cref="IEditingProperty"/>.
        /// </summary>
        public IEditingPropertyInitializer? Initializer { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer"/> that serializes the local value of this <see cref="IEditingProperty"/>.
        /// </summary>
        public IEditingPropertySerializer? Serializer { get; init; }
    }
}