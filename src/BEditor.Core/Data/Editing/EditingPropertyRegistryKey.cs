using System;
using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    ///
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
                EqualityComparer<Type>.Default.Equals(OwnerType, other.OwnerType) &&
                IsDisposable == other.IsDisposable;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Name, OwnerType, IsDisposable);
        }

        /// <inheritdoc/>
        public static bool operator ==(EditingPropertyRegistryKey? left, EditingPropertyRegistryKey? right)
        {
            return EqualityComparer<EditingPropertyRegistryKey>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EditingPropertyRegistryKey? left, EditingPropertyRegistryKey? right)
        {
            return !(left == right);
        }
    }
}
