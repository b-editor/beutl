// EasingMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

using BEditor.LangResources;

namespace BEditor.Data.Property.Easing
{
    /// <summary>
    /// The metadata of <see cref="EasingFunc"/>.
    /// </summary>
    public sealed class EasingMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EasingMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the easing func.</param>
        /// <param name="createFunc">Create a new instance of the <see cref="EasingFunc"/> object.</param>
        /// <param name="type">The type of the object that inherits from <see cref="EasingFunc"/>.</param>
        public EasingMetadata(string name, Func<EasingFunc> createFunc, Type type)
        {
            Name = name;
            CreateFunc = createFunc;
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EasingMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the <see cref="EasingFunc"/>.</param>
        public EasingMetadata(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EasingMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the <see cref="EasingFunc"/>.</param>
        /// <param name="create">This <see cref="Func{TResult}"/> gets a new instance of the <see cref="EasingFunc"/> object.</param>
        public EasingMetadata(string name, Expression<Func<EasingFunc>> create)
            : this(name, create.Compile(), ((NewExpression)create.Body).Type)
        {
        }

        /// <summary>
        /// Gets the loaded <see cref="EasingMetadata"/>.
        /// </summary>
        public static List<EasingMetadata> LoadedEasingFunc { get; } = new();

        /// <summary>
        /// Gets the name of the easing func.
        /// </summary>
        public string Name { get; init; }

#pragma warning disable SA1623
        /// <summary>
        /// Create a new instance of the <see cref="EasingFunc"/> object.
        /// </summary>
        public Func<EasingFunc>? CreateFunc { get; init; }
#pragma warning restore SA1623

        /// <summary>
        /// Gets the type of the object that inherits from <see cref="EasingFunc"/>.
        /// </summary>
        public Type? Type { get; init; }

        /// <summary>
        /// Gets or sets the child elements of the group.
        /// </summary>
        public IEnumerable<EasingMetadata>? Children { get; set; }

        /// <summary>
        /// Create the <see cref="EasingMetadata"/>.
        /// </summary>
        /// <typeparam name="T">The type of object that inherits from EasingFunc.</typeparam>
        /// <param name="name">The name of the easing function.</param>
        /// <returns>A new instance of <see cref="EasingMetadata"/>.</returns>
        public static EasingMetadata Create<T>(string name)
            where T : EasingFunc, new()
        {
            return new(name, () => new T(), typeof(T));
        }

        /// <summary>
        /// Find metadata from types that inherit from <see cref="EasingFunc"/>.
        /// </summary>
        /// <param name="type">Types that inherit from <see cref="EasingFunc"/>.</param>
        /// <returns>The metadata.</returns>
        public static EasingMetadata Find(Type type)
        {
            return FindNullable(type, LoadedEasingFunc) ?? throw new DataException("Not found easing.");
        }

        /// <summary>
        /// Gets the default value of <see cref="EasingMetadata"/>.
        /// </summary>
        /// <returns>Returns the default value of <see cref="EasingMetadata"/>.</returns>
        public static EasingMetadata GetDefault()
        {
            foreach (var item in LoadedEasingFunc)
            {
                if (item.CreateFunc is not null)
                {
                    return item;
                }
                else if (item.Children is not null)
                {
                    var result = GetDefault(item);
                    if (result is not null)
                        return result;
                }
            }

            throw new Exception("No valid easing is registered.");
        }

        private static EasingMetadata? FindNullable(Type type, IEnumerable<EasingMetadata> metadatas)
        {
            foreach (var item in metadatas)
            {
                if (item.Type == type)
                {
                    return item;
                }
                else if (item.Children is not null)
                {
                    var result = FindNullable(type, item.Children);
                    if (result is not null)
                        return result;
                }
            }

            return null;
        }

        private static EasingMetadata? GetDefault(EasingMetadata metadata)
        {
            foreach (var item in metadata.Children!)
            {
                if (item.CreateFunc is not null)
                {
                    return item;
                }
                else if (item.Children is not null)
                {
                    return GetDefault(item);
                }
            }

            return null;
        }
    }
}