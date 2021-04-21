using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Media;

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
        /// <param name="command">Command to be executed.</param>
        public static void Execute(this IRecordCommand command) => CommandManager.Default.Do(command);

        /// <summary>
        /// Execute IRecordCommand with CommandManager specified.
        /// </summary>
        /// <param name="command">Command to be executed.</param>
        /// <param name="manager">The CommandManager to execute.</param>
        public static void Execute(this IRecordCommand command, CommandManager manager) => manager.Do(command);

        /// <summary>
        /// Gets the parent element.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
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
        /// <param name="id">The value of <see cref="IEditingObject.ID"/> to search for.</param>
        /// <returns>The element if found, otherwise the default value of type <typeparamref name="T"/>.</returns>
        [Pure]
        public static T? Find<T>(this IParent<T> self, Guid id)
            where T : IEditingObject
        {
            foreach (var item in self.Children)
            {
                if (item.ID == id)
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
        /// <param name="id">The value of <see cref="IEditingObject.ID"/> to search for.</param>
        /// <returns>The element if found, otherwise the default value of type <typeparamref name="T"/>.</returns>
        [Pure]
        public static T? FindAllChildren<T>(this IParent<object> self, Guid id)
            where T : IEditingObject
        {
            foreach (var item in self.GetAllChildren<T>())
            {
                if (item.ID == id)
                {
                    return item;
                }
            }

            return default;
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="self"></param>
        /// <returns></returns>
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
    }
}