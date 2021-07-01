// ObjectMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

using BEditor.Drawing;

namespace BEditor.Data
{
    /// <summary>
    /// The metadata of <see cref="ObjectElement"/>.
    /// </summary>
    /// <param name="Name">The name of the object element.</param>
    /// <param name="CreateFunc">This <see cref="Func{TResult}"/> gets a new instance of the <see cref="ObjectElement"/> object.</param>
    /// <param name="Type">The type of the object that inherits from <see cref="ObjectElement"/>.</param>
    public record ObjectMetadata(string Name, Func<ObjectElement> CreateFunc, Type Type)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the object element.</param>
        /// <param name="create">This <see cref="Func{TResult}"/> gets a new instance of the <see cref="ObjectElement"/> object.</param>
        public ObjectMetadata(string name, Expression<Func<ObjectElement>> create)
            : this(name, create.Compile(), ((NewExpression)create.Body).Type)
        {
        }

        /// <summary>
        /// Gets or sets the accent color.
        /// </summary>
        public Color AccentColor { get; init; } = Color.FromUInt32(0xff304fee);

        /// <summary>
        /// Gets or sets the path data of an icon.
        /// </summary>
        public string PathIcon { get; init; } = string.Empty;

        /// <summary>
        /// Creates an instance from a file name.
        /// </summary>
        public Func<string, ObjectElement>? CreateFromFile { get; init; }

        /// <summary>
        /// Check to see if the file name is supported.
        /// </summary>
        public Func<string, bool>? IsSupported { get; init; }

        /// <summary>
        /// Gets the loaded <see cref="ObjectMetadata"/>.
        /// </summary>
        public static ObservableCollection<ObjectMetadata> LoadedObjects { get; } = new();

        /// <summary>
        /// Create the <see cref="ObjectMetadata"/>.
        /// </summary>
        /// <typeparam name="T">The type of object that inherits from <see cref="ObjectElement"/>.</typeparam>
        /// <param name="name">The name of the object element.</param>
        /// <returns>A new instance of <see cref="ObjectMetadata"/>.</returns>
        [Obsolete("Use Create{T}(string, Color?, string, Func{string, T}).")]
        public static ObjectMetadata Create<T>(string name)
            where T : ObjectElement, new()
        {
            return new(name, () => new T(), typeof(T));
        }

        /// <summary>
        /// Create the <see cref="ObjectMetadata"/>.
        /// </summary>
        /// <typeparam name="T">The type of object that inherits from <see cref="ObjectElement"/>.</typeparam>
        /// <param name="name">The name of the object element.</param>
        /// <param name="accentColor">The accent color.</param>
        /// <param name="pathIcon">The path data of an icon.</param>
        /// <param name="createFromFile">Creates an instance from a file name.</param>
        /// <param name="isSupported">Check to see if the file name is supported.</param>
        /// <returns>A new instance of <see cref="ObjectMetadata"/>.</returns>
        public static ObjectMetadata Create<T>(string name, Color? accentColor = null, string pathIcon = "", Func<string, T>? createFromFile = null, Func<string, bool>? isSupported = null)
            where T : ObjectElement, new()
        {
            return new(name, () => new T(), typeof(T))
            {
                AccentColor = accentColor ?? Color.FromUInt32(0xff304fee),
                PathIcon = pathIcon,
                CreateFromFile = createFromFile,
                IsSupported = isSupported,
            };
        }
    }
}