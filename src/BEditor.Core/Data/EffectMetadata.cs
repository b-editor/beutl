using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace BEditor.Data
{
    /// <summary>
    /// The metadata of <see cref="EffectElement"/>.
    /// </summary>
    /// <param name="Name">The name of the effect element.</param>
    /// <param name="CreateFunc">This <see cref="Func{TResult}"/> gets a new instance of the <see cref="EffectElement"/> object.</param>
    /// <param name="Type">The type of the object that inherits from <see cref="EffectElement"/>.</param>
    public record EffectMetadata(string Name, Func<EffectElement> CreateFunc, Type Type)
    {
        /// <summary>
        /// The metadata of <see cref="EffectElement"/>.
        /// </summary>
        /// <param name="Name">The name of the effect element.</param>
        public EffectMetadata(string Name)
            : this(Name, () => new EffectElement.EmptyClass())
        {
        }

        /// <summary>
        /// The metadata of <see cref="EffectElement"/>.
        /// </summary>
        /// <param name="Name">The name of the effect element.</param>
        /// <param name="Create">This <see cref="Func{TResult}"/> gets a new instance of the <see cref="EffectElement"/> object.</param>
        public EffectMetadata(string Name, Expression<Func<EffectElement>> Create)
            : this(Name, Create.Compile(), ((NewExpression)Create.Body).Type)
        {
        }

        /// <summary>
        /// Gets or sets the child elements of the group.
        /// </summary>
        public IEnumerable<EffectMetadata>? Children { get; set; }

        /// <summary>
        /// Gets the loaded <see cref="EffectMetadata"/>.
        /// </summary>
        public static ObservableCollection<EffectMetadata> LoadedEffects { get; } = new();

        /// <summary>
        /// Create the <see cref="EffectMetadata"/>.
        /// </summary>
        /// <typeparam name="T">The type of object that inherits from <see cref="EffectElement"/>.</typeparam>
        /// <param name="Name">The name of the effect element.</param>
        /// <returns>A new instance of <see cref="EffectMetadata"/>.</returns>
        public static EffectMetadata Create<T>(string Name)
            where T : EffectElement, new()
        {
            return new(Name, () => new T(), typeof(T));
        }
    }
}