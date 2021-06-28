// DirectProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data
{
    /// <summary>
    /// A direct editing property.
    /// </summary>
    /// <typeparam name="TOwner">The type of the owner.</typeparam>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public class DirectProperty<TOwner, TValue> : EditingProperty<TValue>, IDirectProperty<TValue>
        where TOwner : IEditingObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectProperty{TOwner, TValue}"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="getter">Gets the current value of the property.</param>
        /// <param name="setter">Sets the value of the property.</param>
        public DirectProperty(string name, Func<TOwner, TValue> getter, Action<TOwner, TValue> setter)
            : base(name, typeof(TOwner))
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
        /// <param name="options">The property options.</param>
        /// <returns>Returns an instance of the editing property registered by this method.</returns>
        public DirectProperty<TNewOwner, TValue> WithOwner<TNewOwner>(
            Func<TNewOwner, TValue> getter,
            Action<TNewOwner, TValue> setter,
            EditingPropertyOptions<TValue> options = default)
            where TNewOwner : IEditingObject
        {
            var property = new DirectProperty<TNewOwner, TValue>(Name, getter, setter)
            {
                Initializer = options.Initializer ?? Initializer,
                Serializer = options.Serializer ?? Serializer,
                IsDisposable = IsDisposable,
            };

            EditingPropertyRegistry.Register(typeof(TNewOwner), property);

            return property;
        }
    }
}