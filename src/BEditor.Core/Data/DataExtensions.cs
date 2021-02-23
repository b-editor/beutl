using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
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
        /// <param name="command">Command to be executed</param>
        public static void Execute(this IRecordCommand command) => CommandManager.Do(command);

        /// <summary>
        /// Activate this <see cref="IElementObject"/> and set metadata
        /// </summary>
        public static void Load(this PropertyElement property, PropertyElementMetadata metadata)
        {
            property.Load();
            property.PropertyMetadata = metadata;
        }
        /// <summary>
        /// Activate this <see cref="IElementObject"/> and set metadata
        /// </summary>
        public static void Load<T>(this PropertyElement<T> property, T metadata) where T : PropertyElementMetadata
        {
            property.Load();
            property.PropertyMetadata = metadata;
        }

        /// <summary>
        /// Get the parent element.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve</typeparam>
        /// <param name="self"></param>
        [Pure] public static T? GetParent<T>(this IChild<T> self) => self.Parent;
        /// <summary>
        /// Get the parent element one level ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve</typeparam>
        /// <param name="self"></param>
        [Pure] public static T? GetParent2<T>(this IChild<IChild<T>> self) => self.Parent!.Parent;
        /// <summary>
        /// Get the parent element two steps ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve</typeparam>
        /// <param name="self"></param>
        [Pure] public static T? GetParent3<T>(this IChild<IChild<IChild<T>>> self) => self.Parent!.Parent!.Parent;
        /// <summary>
        /// Get the parent element three levels ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve</typeparam>
        /// <param name="self"></param>
        [Pure] public static T? GetParent4<T>(this IChild<IChild<IChild<IChild<T>>>> self) => self.Parent!.Parent!.Parent!.Parent;
        /// <summary>
        /// Get the parent element four levels ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve</typeparam>
        /// <param name="self"></param>
        [Pure] public static T? GetParent5<T>(this IChild<IChild<IChild<IChild<IChild<T>>>>> self) => self.Parent!.Parent!.Parent!.Parent!.Parent;

        /// <summary>
        /// Searches for child elements by id.
        /// </summary>
        /// <typeparam name="T">Type of the child element</typeparam>
        /// <param name="self"></param>
        /// <param name="id">The value of <see cref="IHasId.Id"/> to search for</param>
        /// <returns>The element if found, otherwise the default value of type <typeparamref name="T"/>.</returns>
        [Pure]
        public static T? Find<T>(this IParent<T> self, int id) where T : IHasId
        {
            if (self.Children is T[] array)
            {
                return Array.Find(array, t => t.Id == id);
            }
            else if (self.Children is List<T> list)
            {
                list.Find(t => t.Id == id);
            }

            return self.Children.ToList().Find(item => item.Id == id);
        }
        /// <summary>
        /// Searches for child elements by name.
        /// </summary>
        /// <typeparam name="T">Type of the child element</typeparam>
        /// <param name="self"></param>
        /// <param name="name">The value of <see cref="IHasName.Name"/> to search for</param>
        /// <returns>The element if found, otherwise the default value of type <typeparamref name="T"/>.</returns>
        [Pure]
        public static T? Find<T>(this IParent<T> self, string name) where T : IHasName
        {
            if (self.Children is T[] array)
            {
                return Array.Find(array, t => t.Name == name);
            }
            else if (self.Children is List<T> list)
            {
                list.Find(t => t.Name == name);
            }

            return self.Children.ToList().Find(t => t.Name == name);
        }
        /// <summary>
        /// Determines whether an element is in the <see cref="IParent{T}"/>.
        /// </summary>
        /// <typeparam name="T">Type of the child element</typeparam>
        /// <param name="self"></param>
        /// <param name="item">The object to locate in the <see cref="IParent{T}"/>. The value can be null for reference types.</param>
        /// <returns>true if item is found in the <see cref="IParent{T}"/>; otherwise, false.</returns>
        [Pure]
        public static bool Contains<T>(this IParent<T> self, T item)
        {
            return self.Children.Contains(item);
        }

        internal static int IndexOf(this IEnumerable<PropertyElement> self, PropertyElement prop)
        {
            var count = -1;
            foreach (var item in self)
            {
                count++;

                if (item == prop)
                {
                    return count;
                }

                if (item is IParent<PropertyElement> inner_prop)
                {
                    var inner_count = -1;
                    foreach (var inner_item in inner_prop.Children)
                    {
                        inner_count++;

                        if (inner_item == prop)
                        {
                            return inner_count;
                        }
                    }
                }
            }

            return -1;
        }
    }
}
