// DataExtensions.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

using BEditor.Command;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a class that provides an extension method.
    /// </summary>
    public static class DataExtensions
    {
        /// <summary>
        /// Execute IRecordCommand.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public static void Execute(this IRecordCommand command)
        {
            CommandManager.Default.Do(command);
        }

        /// <summary>
        /// Execute IRecordCommand with CommandManager specified.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="manager">The command manager for executing commands.</param>
        public static void Execute(this IRecordCommand command, CommandManager manager)
        {
            manager.Do(command);
        }

        /// <summary>
        /// Gets the parent element.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        /// <returns>Returns the parent element.</returns>
        [Pure]
        public static T GetRequiredParent<T>(this IChild<object> self)
        {
            var parent = GetParent<T>(self);
            if (parent is null) throw new DataException();

            return parent;
        }

        /// <summary>
        /// Gets the parent element.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        /// <returns>Returns the parent element.</returns>
        [Pure]
        public static T? GetParent<T>(this IChild<object> self)
        {
            object obj = self;

            while (obj is not T)
            {
                if (obj is IChild<object> c)
                {
                    obj = c.Parent;
                }
                else if (obj is null)
                {
                    return default;
                }
            }

            return (T)obj;
        }

        /// <summary>
        /// Gets the root element.
        /// </summary>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        /// <returns>Returns the root element.</returns>
        [Pure]
        public static object? GetRoot(this IChild<object> self)
        {
            object obj = self;

            while (obj is not null)
            {
                if (obj is IChild<object> c)
                {
                    obj = c.Parent;

                    if (obj is null)
                    {
                        return c;
                    }
                }
            }

            return obj;
        }

        /// <summary>
        /// Searches for child elements by id.
        /// </summary>
        /// <typeparam name="T">Type of the child element.</typeparam>
        /// <param name="self">The parent element containing the child elements to be searched.</param>
        /// <param name="id">The value of <see cref="IEditingObject.Id"/> to search for.</param>
        /// <returns>The element if found, otherwise the default value of type <typeparamref name="T"/>.</returns>
        [Pure]
        public static T? Find<T>(this IParent<T> self, Guid id)
            where T : IEditingObject
        {
            foreach (var item in self.Children)
            {
                if (item.Id == id)
                {
                    return item;
                }
            }

            return default;
        }

        /// <summary>
        /// Searches for child elements by id.
        /// </summary>
        /// <typeparam name="T">Type of the child element.</typeparam>
        /// <param name="self">The parent element containing the child elements to be searched.</param>
        /// <param name="id">The value of <see cref="IEditingObject.Id"/> to search for.</param>
        /// <returns>The element if found, otherwise the default value of type <typeparamref name="T"/>.</returns>
        [Pure]
        public static T? FindAllChildren<T>(this IParent<object> self, Guid id)
            where T : IEditingObject
        {
            foreach (var item in self.GetAllChildren<T>())
            {
                if (item.Id == id)
                {
                    return item;
                }
            }

            return default;
        }

        /// <summary>
        /// Enumerates the objects of a given type within a given object.
        /// </summary>
        /// <typeparam name="TResult">The type of the object to enumerate.</typeparam>
        /// <param name="self">An instance of <see cref="IParent{T}"/> that contains the object to be enumerated.</param>
        /// <returns>Returns the objects of a given type within a given object.</returns>
        [Pure]
        public static IEnumerable<TResult> GetAllChildren<TResult>(this IParent<object> self)
        {
            foreach (var item in self.Children)
            {
                if (item is IParent<object> innerParent)
                {
                    foreach (var innerItem in GetAllChildren<TResult>(innerParent))
                    {
                        yield return innerItem;
                    }
                }

                if (item is TResult t) yield return t;
            }
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <typeparam name="TValue">The type of the property value.</typeparam>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<TValue> Serialize<TValue>(this EditingPropertyOptions<TValue> options)
            where TValue : IJsonObject
        {
            return options.Serialize(Internals.PropertyJsonSerializer<TValue>.Current);
        }
    }
}