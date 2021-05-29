// EditingPropertyRegistry.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;

using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    ///  Tracks registered <see cref="EditingProperty"/> instances.
    /// </summary>
    public static class EditingPropertyRegistry
    {
        internal static readonly Dictionary<EditingPropertyRegistryKey, EditingProperty> _registered = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Register the <see cref="EditingProperty"/>.
        /// </summary>
        /// <param name="key">The key of the property to register.</param>
        /// <param name="property">The property to register.</param>
        /// <exception cref="DataException">This key has already been registered.</exception>
        public static void Register(EditingPropertyRegistryKey key, EditingProperty property)
        {
            lock (_lock)
            {
                if (_registered.ContainsKey(key))
                {
                    throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
                }

                _registered.Add(key, property);
            }
        }

        /// <summary>
        /// Gets the editing properties of the specified type.
        /// </summary>
        /// <typeparam name="T">The type that contains the property to be retrieved.</typeparam>
        /// <returns>The properties contained in the specified type.</returns>
        public static IEnumerable<EditingProperty> GetProperties<T>()
        {
            return _registered
                .AsParallel()
                .Where(static i => typeof(T).IsAssignableTo(i.Key.OwnerType))
                .Select(static i => i.Value);
        }

        /// <summary>
        /// Gets the editing properties of the specified type.
        /// </summary>
        /// <param name="type">The type that contains the property to be retrieved.</param>
        /// <returns>The properties contained in the specified type.</returns>
        public static IEnumerable<EditingProperty> GetProperties(Type type)
        {
            return _registered
                .AsParallel()
                .Where(i => type.IsAssignableTo(i.Key.OwnerType))
                .Select(static i => i.Value);
        }

        /// <summary>
        /// Gets the serializable editing properties of the specified type.
        /// </summary>
        /// <typeparam name="T">The type that contains the property to be retrieved.</typeparam>
        /// <returns>The properties contained in the specified type.</returns>
        public static IEnumerable<EditingProperty> GetSerializableProperties<T>()
        {
            return _registered
                .AsParallel()
                .AsOrdered()
                .Where(static i => i.Value.Serializer is not null && typeof(T).IsAssignableTo(i.Key.OwnerType))
                .Select(static i => i.Value);
        }

        /// <summary>
        /// Gets the serializable editing properties of the specified type.
        /// </summary>
        /// <param name="type">The type that contains the property to be retrieved.</param>
        /// <returns>The properties contained in the specified type.</returns>
        public static IEnumerable<EditingProperty> GetSerializableProperties(Type type)
        {
            return _registered
                .AsParallel()
                .AsOrdered()
                .Where(i => i.Value.Serializer is not null && type.IsAssignableTo(i.Key.OwnerType))
                .Select(static i => i.Value);
        }

        /// <summary>
        /// Gets automatically initializable editing properties of the specified type.
        /// </summary>
        /// <typeparam name="T">The type that contains the property to be retrieved.</typeparam>
        /// <returns>The properties contained in the specified type.</returns>
        public static IEnumerable<EditingProperty> GetInitializableProperties<T>()
        {
            return _registered
                .AsParallel()
                .Where(static i => i.Value is IDirectProperty && i.Value.Initializer is not null && typeof(T).IsAssignableTo(i.Key.OwnerType))
                .Select(static i => i.Value);
        }

        /// <summary>
        /// Gets automatically initializable editing properties of the specified type.
        /// </summary>
        /// <param name="type">The type that contains the property to be retrieved.</param>
        /// <returns>The properties contained in the specified type.</returns>
        public static IEnumerable<EditingProperty> GetInitializableProperties(Type type)
        {
            return _registered
                .AsParallel()
                .Where(i => i.Value is IDirectProperty && i.Value.Initializer is not null && type.IsAssignableTo(i.Key.OwnerType))
                .Select(static i => i.Value);
        }

        /// <summary>
        /// Gets whether the property has already been registered.
        /// </summary>
        /// <param name="key">The key of the property to check if the property is already registered.</param>
        /// <returns>Returns <see langword="true"/> if already registered, <see langword="false"/> otherwise.</returns>
        public static bool IsRegistered(EditingPropertyRegistryKey key)
        {
            lock (_lock)
            {
                return _registered.ContainsKey(key);
            }
        }

        internal static void RegisterUnChecked(EditingPropertyRegistryKey key, EditingProperty property)
        {
            _registered.Add(key, property);
        }
    }
}