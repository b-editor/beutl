using System;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the local value.</typeparam>
    public class EditingProperty<TValue> : EditingProperty
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty{TValue}"/> class.
        /// </summary>
        /// <param name="name">プロパティの名前です.</param>
        /// <param name="owner">このプロパティを持つオブジェクトの <see cref="Type"/> です.</param>
        /// <param name="builder">プロパティの値を初期化するオブジェクトです.</param>
        internal EditingProperty(string name, Type owner, IPropertyBuilder<TValue>? builder = null)
            : base(name, owner, typeof(TValue), builder)
        {
        }

        /// <summary>
        /// Gets the <see cref="IPropertyBuilder{T}"/> that initializes the local value of a property.
        /// </summary>
        public new IPropertyBuilder<TValue>? Builder => base.Builder as IPropertyBuilder<TValue>;
    }
}
