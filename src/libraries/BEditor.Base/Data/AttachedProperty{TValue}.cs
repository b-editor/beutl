// AttachedProperty{TValue}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data
{
    internal interface IAttachedProperty : IEditingProperty
    {
    }

    /// <summary>
    /// An attached editing property.
    /// </summary>
    /// <typeparam name="TValue">The type of the property's value.</typeparam>
    public class AttachedProperty<TValue> : EditingProperty<TValue>, IAttachedProperty
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AttachedProperty{TValue}"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="ownerType">The class that is registering the property.</param>
        public AttachedProperty(string name, Type ownerType)
            : base(name, ownerType)
        {
        }

        /// <summary>
        /// Registers the attached property on another type.
        /// </summary>
        /// <typeparam name="TNewOwner">The type of the owner.</typeparam>
        /// <param name="options">The property options.</param>
        /// <returns>Returns an instance of the editing property registered by this method.</returns>
        public AttachedProperty<TValue> WithOwner<TNewOwner>(
            EditingPropertyOptions<TValue> options = default)
            where TNewOwner : IEditingObject
        {
            var property = new AttachedProperty<TValue>(Name, typeof(TNewOwner))
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