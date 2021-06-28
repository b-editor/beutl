// EditingPropertyRegistry.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    ///  Tracks registered <see cref="EditingProperty"/> instances.
    /// </summary>
    public static class EditingPropertyRegistry
    {
        private static readonly Dictionary<int, EditingProperty> _properties = new();
        private static readonly Dictionary<Type, Dictionary<int, EditingProperty>> _registered = new();
        private static readonly Dictionary<Type, Dictionary<int, EditingProperty>> _attached = new();
        private static readonly Dictionary<Type, Dictionary<int, EditingProperty>> _direct = new();
        private static readonly Dictionary<Type, List<EditingProperty>> _registeredCache = new();
        private static readonly Dictionary<Type, List<EditingProperty>> _attachedCache = new();
        private static readonly Dictionary<Type, List<EditingProperty>> _directCache = new();

        /// <summary>
        /// Gets all non-attached <see cref="EditingProperty"/>s registered on a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>A collection of <see cref="EditingProperty"/> definitions.</returns>
        public static IReadOnlyList<EditingProperty> GetRegistered(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (_registeredCache.TryGetValue(type, out var result))
            {
                return result;
            }

            var t = type;
            result = new List<EditingProperty>();

            while (t != null)
            {
                RuntimeHelpers.RunClassConstructor(t.TypeHandle);

                if (_registered.TryGetValue(t, out var registered))
                {
                    result.AddRange(registered.Values);
                }

                t = t.BaseType;
            }

            _registeredCache.Add(type, result);
            return result;
        }

        /// <summary>
        /// Gets all attached <see cref="EditingProperty"/>s registered on a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>A collection of <see cref="EditingProperty"/> definitions.</returns>
        public static IReadOnlyList<EditingProperty> GetRegisteredAttached(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (_attachedCache.TryGetValue(type, out var result))
            {
                return result;
            }

            var t = type;
            result = new List<EditingProperty>();

            while (t != null)
            {
                if (_attached.TryGetValue(t, out var attached))
                {
                    result.AddRange(attached.Values);
                }

                t = t.BaseType;
            }

            _attachedCache.Add(type, result);
            return result;
        }

        /// <summary>
        /// Gets all direct <see cref="EditingProperty"/>s registered on a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>A collection of <see cref="EditingProperty"/> definitions.</returns>
        public static IReadOnlyList<EditingProperty> GetRegisteredDirect(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));

            if (_directCache.TryGetValue(type, out var result))
            {
                return result;
            }

            var t = type;
            result = new List<EditingProperty>();

            while (t != null)
            {
                if (_direct.TryGetValue(t, out var direct))
                {
                    result.AddRange(direct.Values);
                }

                t = t.BaseType;
            }

            _directCache.Add(type, result);
            return result;
        }

        /// <summary>
        /// Gets all <see cref="EditingProperty"/>s registered on a object.
        /// </summary>
        /// <param name="o">The object.</param>
        /// <returns>A collection of <see cref="EditingProperty"/> definitions.</returns>
        public static IReadOnlyList<EditingProperty> GetRegistered(IEditingProperty o)
        {
            if (o is null) throw new ArgumentNullException(nameof(o));
            return GetRegistered(o.GetType());
        }

        /// <summary>
        /// Finds a registered property on a type by name.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The property name.</param>
        /// <returns>
        /// The registered property or null if no matching property found.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The property name contains a '.'.
        /// </exception>
        public static EditingProperty? FindRegistered(Type type, string name)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Contains("."))
            {
                throw new InvalidOperationException("Attached properties not supported.");
            }

            var registered = GetRegistered(type);
            var registeredCount = registered.Count;

            for (var i = 0; i < registeredCount; i++)
            {
                var x = registered[i];

                if (x.Name == name)
                {
                    return x;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a registered property on an object by name.
        /// </summary>
        /// <param name="o">The object.</param>
        /// <param name="name">The property name.</param>
        /// <returns>
        /// The registered property or null if no matching property found.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The property name contains a '.'.
        /// </exception>
        public static EditingProperty? FindRegistered(IEditingObject o, string name)
        {
            if (o is null) throw new ArgumentNullException(nameof(o));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException($"'{nameof(name)}' cannot be null or empty.", nameof(name));
            return FindRegistered(o.GetType(), name);
        }

        /// <summary>
        /// Finds a registered property by Id.
        /// </summary>
        /// <param name="id">The property Id.</param>
        /// <returns>The registered property or null if no matching property found.</returns>
        public static EditingProperty? FindRegistered(int id)
        {
            return id < _properties.Count ? _properties[id] : null;
        }

        /// <summary>
        /// Checks whether a <see cref="EditingProperty"/> is registered on a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="property">The property.</param>
        /// <returns>True if the property is registered, otherwise false.</returns>
        public static bool IsRegistered(Type type, EditingProperty property)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (property is null) throw new ArgumentNullException(nameof(property));

            static bool ContainsProperty(IReadOnlyList<EditingProperty> properties, EditingProperty property)
            {
                var propertiesCount = properties.Count;

                for (var i = 0; i < propertiesCount; i++)
                {
                    if (properties[i] == property)
                    {
                        return true;
                    }
                }

                return false;
            }

            return ContainsProperty(GetRegistered(type), property) ||
                   ContainsProperty(GetRegisteredAttached(type), property);
        }

        /// <summary>
        /// Checks whether a <see cref="EditingProperty"/> is registered on a object.
        /// </summary>
        /// <param name="o">The object.</param>
        /// <param name="property">The property.</param>
        /// <returns>True if the property is registered, otherwise false.</returns>
        public static bool IsRegistered(object o, EditingProperty property)
        {
            if (o is null) throw new ArgumentNullException(nameof(o));
            if (property is null) throw new ArgumentNullException(nameof(property));
            return IsRegistered(o.GetType(), property);
        }

        /// <summary>
        /// Registers a <see cref="EditingProperty"/> on a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="property">The property.</param>
        /// <remarks>
        /// You won't usually want to call this method directly, instead use the
        /// <see cref="EditingProperty.Register{TValue, TOwner}(string, EditingPropertyOptions{TValue})"/>
        /// method.
        /// </remarks>
        public static void Register(Type type, EditingProperty property)
        {
            if (property is null) throw new ArgumentNullException(nameof(property));
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (!_registered.TryGetValue(type, out var inner))
            {
                inner = new Dictionary<int, EditingProperty>
                {
                    { property.Id, property },
                };
                _registered.Add(type, inner);
            }
            else if (!inner.ContainsKey(property.Id))
            {
                inner.Add(property.Id, property);
            }

            if (property is IDirectProperty)
            {
                if (!_direct.TryGetValue(type, out inner))
                {
                    inner = new Dictionary<int, EditingProperty>
                    {
                        { property.Id, property },
                    };
                    _direct.Add(type, inner);
                }
                else if (!inner.ContainsKey(property.Id))
                {
                    inner.Add(property.Id, property);
                }

                _directCache.Clear();
            }

            if (!_properties.ContainsKey(property.Id))
            {
                _properties.Add(property.Id, property);
            }

            _registeredCache.Clear();
        }

        /// <summary>
        /// Registers an attached <see cref="EditingProperty"/> on a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="property">The property.</param>
        /// <remarks>
        /// You won't usually want to call this method directly, instead use the
        /// <see cref="EditingProperty.RegisterAttached{TValue, TOwner}(string, EditingPropertyOptions{TValue})"/>
        /// method.
        /// </remarks>
        public static void RegisterAttached(Type type, EditingProperty property)
        {
            if (property is not IAttachedProperty)
            {
                throw new InvalidOperationException(
                    "Cannot register a non-attached property as attached.");
            }

            if (!_attached.TryGetValue(type, out var inner))
            {
                inner = new Dictionary<int, EditingProperty>
                {
                    { property.Id, property },
                };
                _attached.Add(type, inner);
            }
            else
            {
                inner.Add(property.Id, property);
            }

            if (!_properties.ContainsKey(property.Id))
            {
                _properties.Add(property.Id, property);
            }

            _attachedCache.Clear();
        }
    }
}