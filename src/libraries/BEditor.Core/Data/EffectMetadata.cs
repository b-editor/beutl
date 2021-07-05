// EffectMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace BEditor.Data
{
    /// <summary>
    /// The metadata of <see cref="EffectElement"/>.
    /// </summary>
    public class EffectMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the effect element.</param>
        /// <param name="createFunc">Create a new instance of the <see cref="EffectElement"/> object.</param>
        /// <param name="type">The type of the object that inherits from <see cref="EffectElement"/>.</param>
        public EffectMetadata(string name, Func<EffectElement> createFunc, Type type)
        {
            Name = name;
            CreateFunc = createFunc;
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EffectMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the <see cref="EffectElement"/>.</param>
        public EffectMetadata(string name)
            : this(name, () => new EffectElement.EmptyClass())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EffectMetadata"/> class.
        /// </summary>
        /// <param name="name">The name of the <see cref="EffectElement"/>.</param>
        /// <param name="create">This <see cref="Func{TResult}"/> gets a new instance of the <see cref="EffectElement"/> object.</param>
        public EffectMetadata(string name, Expression<Func<EffectElement>> create)
            : this(name, create.Compile(), ((NewExpression)create.Body).Type)
        {
        }

        /// <summary>
        /// Gets the loaded <see cref="EffectMetadata"/>.
        /// </summary>
        public static ObservableCollection<EffectMetadata> LoadedEffects { get; } = new();

        /// <summary>
        /// Gets or sets the name of the effect element.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Create a new instance of the <see cref="EffectElement"/> object.
        /// </summary>
        public Func<EffectElement> CreateFunc { get; init; }

        /// <summary>
        /// Gets or sets the type of the object that inherits from <see cref="EffectElement"/>.
        /// </summary>
        public Type Type { get; init; }

        /// <summary>
        /// Gets or sets the child elements of the group.
        /// </summary>
        public IEnumerable<EffectMetadata>? Children { get; set; }

        /// <summary>
        /// Create the <see cref="EffectMetadata"/>.
        /// </summary>
        /// <typeparam name="T">The type of object that inherits from <see cref="EffectElement"/>.</typeparam>
        /// <param name="name">The name of the effect element.</param>
        /// <returns>A new instance of <see cref="EffectMetadata"/>.</returns>
        public static EffectMetadata Create<T>(string name)
            where T : EffectElement, new()
        {
            return new(name, () => new T(), typeof(T));
        }
    }
}