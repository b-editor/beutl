// DirectEditingProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    /// A direct editing property.
    /// </summary>
    /// <typeparam name="TOwner">The type of the owner.</typeparam>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public class DirectEditingProperty<TOwner, TValue> : EditingProperty<TValue>, IDirectProperty<TValue>
        where TOwner : IEditingObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectEditingProperty{TOwner, TValue}"/> class.
        /// </summary>
        /// <param name="getter">Gets the current value of the property.</param>
        /// <param name="setter">Sets the value of the property.</param>
        /// <param name="key">The registry key.</param>
        public DirectEditingProperty(Func<TOwner, TValue> getter, Action<TOwner, TValue> setter, EditingPropertyRegistryKey key)
            : base(key)
        {
            (Getter, Setter) = (getter, setter);
        }

        /// <summary>
        /// Gets the getter function.
        /// </summary>
        public Func<TOwner, TValue> Getter { get; }

        /// <summary>
        /// Gets the setter function.
        /// </summary>
        public Action<TOwner, TValue> Setter { get; }

        /// <inheritdoc/>
        TValue IDirectProperty<TValue>.Get(IEditingObject owner)
        {
            return Getter((TOwner)owner)!;
        }

        /// <inheritdoc/>
        void IDirectProperty<TValue>.Set(IEditingObject owner, TValue value)
        {
            Setter((TOwner)owner, value);
        }

        /// <summary>
        /// Registers the direct property on another type.
        /// </summary>
        /// <typeparam name="TNewOwner">The type of the owner.</typeparam>
        /// <param name="getter">Gets the current value of the property.</param>
        /// <param name="setter">Sets the value of the property.</param>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <param name="serializer">To serialize this property, specify the serializer.</param>
        /// <returns>Returns an instance of the editing property registered by this method.</returns>
        public DirectEditingProperty<TNewOwner, TValue> WithOwner<TNewOwner>(
            Func<TNewOwner, TValue> getter,
            Action<TNewOwner, TValue> setter,
            IEditingPropertyInitializer<TValue>? initializer = null,
            IEditingPropertySerializer<TValue>? serializer = null)
            where TNewOwner : IEditingObject
        {
            var key = new EditingPropertyRegistryKey(Name, typeof(TNewOwner), Key.IsDisposable);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            initializer ??= Initializer;
            serializer ??= Serializer;

            var property = new DirectEditingProperty<TNewOwner, TValue>(getter, setter, key)
            {
                Initializer = initializer,
                Serializer = serializer,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

            return property;
        }
    }
}