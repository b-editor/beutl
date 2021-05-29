// EditingPropertyRegistryKey.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the key used in <see cref="EditingPropertyRegistry"/>.
    /// </summary>
    public sealed class EditingPropertyRegistryKey : IEquatable<EditingPropertyRegistryKey?>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditingPropertyRegistryKey"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="ownerType">The owner type of the property.</param>
        /// <param name="isDisposable">The value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.</param>
        public EditingPropertyRegistryKey(string name, Type ownerType, bool isDisposable)
        {
            (Name, OwnerType, IsDisposable) = (name, ownerType, isDisposable);
        }

        /// <summary>
        /// Gets the name of the property.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the owner type of the property.
        /// </summary>
        public Type OwnerType { get; }

        /// <summary>
        /// Gets the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.
        /// </summary>
        public bool IsDisposable { get; }

        /// <summary>
        /// Compares two <see cref="EditingPropertyRegistryKey"/>. The result specifies whether the values of the two <see cref="EditingPropertyRegistryKey"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="EditingPropertyRegistryKey"/> to compare.</param>
        /// <param name="right">A <see cref="EditingPropertyRegistryKey"/> to compare.</param>
        /// <returns>true if the left and right <see cref="EditingPropertyRegistryKey"/> are equal; otherwise, false.</returns>
        public static bool operator ==(EditingPropertyRegistryKey? left, EditingPropertyRegistryKey? right)
        {
            return EqualityComparer<EditingPropertyRegistryKey>.Default.Equals(left, right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="EditingPropertyRegistryKey"/> to compare.</param>
        /// <param name="right">A <see cref="EditingPropertyRegistryKey"/> to compare.</param>
        /// <returns>true if the left and right <see cref="EditingPropertyRegistryKey"/> differ; otherwise, false.</returns>
        public static bool operator !=(EditingPropertyRegistryKey? left, EditingPropertyRegistryKey? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as EditingPropertyRegistryKey);
        }

        /// <inheritdoc/>
        public bool Equals(EditingPropertyRegistryKey? other)
        {
            return other != null &&
                Name == other.Name &&
                OwnerType == other.OwnerType &&
                IsDisposable == other.IsDisposable;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Name, OwnerType, IsDisposable);
        }
    }
}