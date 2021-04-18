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
        /// Activate this <see cref="IElementObject"/> and set metadata.
        /// </summary>
        public static void Load(this PropertyElement property, PropertyElementMetadata metadata)
        {
            property.Load();
            property.PropertyMetadata = metadata;
        }

        /// <summary>
        /// Activate this <see cref="IElementObject"/> and set metadata.
        /// </summary>
        /// <typeparam name="T">Type of <see cref="PropertyElement{T}.PropertyMetadata"/>.</typeparam>
        public static void Load<T>(this PropertyElement<T> property, T metadata)
            where T : PropertyElementMetadata
        {
            property.Load();
            property.PropertyMetadata = metadata;
        }

        /// <summary>
        /// Get the parent element.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        [Pure]
        public static T? GetParent<T>(this IChild<T> self) => self.Parent;

        /// <summary>
        /// Get the parent element one level ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        [Pure]
        public static T? GetParent2<T>(this IChild<IChild<T>> self) => self.Parent!.Parent;

        /// <summary>
        /// Get the parent element two steps ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        [Pure]
        public static T? GetParent3<T>(this IChild<IChild<IChild<T>>> self) => self.Parent!.Parent!.Parent;

        /// <summary>
        /// Get the parent element three levels ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        [Pure]
        public static T? GetParent4<T>(this IChild<IChild<IChild<IChild<T>>>> self) => self.Parent!.Parent!.Parent!.Parent;

        /// <summary>
        /// Get the parent element four levels ahead.
        /// </summary>
        /// <typeparam name="T">Type of the parent element to retrieve.</typeparam>
        /// <param name="self">The child element of the parent element to retrieve.</param>
        [Pure]
        public static T? GetParent5<T>(this IChild<IChild<IChild<IChild<IChild<T>>>>> self) => self.Parent!.Parent!.Parent!.Parent!.Parent;

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
    }
}