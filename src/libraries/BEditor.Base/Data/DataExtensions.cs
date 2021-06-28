// DataExtensions.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a class that provides an extension method.
    /// </summary>
    public static class DataExtensions
    {
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
                else
                {
                    break;
                }
            }

            if (obj is T result)
            {
                return result;
            }
            else
            {
                return default;
            }
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

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<string?> Serialize(this EditingPropertyOptions<string?> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteStringValue(value),
                element => element.GetString() ?? string.Empty);
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<int> Serialize(this EditingPropertyOptions<int> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetInt32());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<long> Serialize(this EditingPropertyOptions<long> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetInt64());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<uint> Serialize(this EditingPropertyOptions<uint> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetUInt32());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<ulong> Serialize(this EditingPropertyOptions<ulong> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetUInt64());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<float> Serialize(this EditingPropertyOptions<float> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetSingle());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<double> Serialize(this EditingPropertyOptions<double> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetDouble());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<decimal> Serialize(this EditingPropertyOptions<decimal> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteNumberValue(value),
                element => element.GetDecimal());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<Guid> Serialize(this EditingPropertyOptions<Guid> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteStringValue(value),
                element => element.GetGuid());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<DateTime> Serialize(this EditingPropertyOptions<DateTime> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteStringValue(value),
                element => element.GetDateTime());
        }

        /// <summary>
        /// Specifies the serializer for the property.
        /// </summary>
        /// <param name="options">The options to be specified.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<DateTimeOffset> Serialize(this EditingPropertyOptions<DateTimeOffset> options)
        {
            return options.Serialize(
                (writer, value) => writer.WriteStringValue(value),
                element => element.GetDateTimeOffset());
        }

        /// <summary>
        /// Gets an observable for a <see cref="EditingObject"/>.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <typeparam name="T">The property type.</typeparam>
        /// <param name="property">The property.</param>
        /// <returns>
        /// An observable which fires immediately with the current value of the property on the
        /// object and subsequently each time the property value changes.
        /// </returns>
        public static IObservable<T> GetObservable<T>(this IEditingObject obj, EditingProperty<T> property)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            if (property is null) throw new ArgumentNullException(nameof(property));

            return new EditingObjectSubject<T>(obj, property);
        }

        /// <summary>
        /// Gets a subject for a <see cref="EditingObject"/>.
        /// </summary>
        /// <typeparam name="T">The property type.</typeparam>
        /// <param name="obj">The object.</param>
        /// <param name="property">The property.</param>
        /// <returns>
        /// An <see cref="ISubject{T}"/> which can be used for two-way binding to/from the property.
        /// </returns>
        public static ISubject<T> GetSubject<T>(this IEditingObject obj, EditingProperty<T> property)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            if (property is null) throw new ArgumentNullException(nameof(property));

            return new EditingObjectSubject<T>(obj, property);
        }
    }
}