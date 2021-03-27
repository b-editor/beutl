using System;
using System.Collections.Generic;

using BEditor.Properties;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public class EditingProperty
    {
        /// <summary>
        /// 登録された全ての <see cref="EditingProperty"/> です.
        /// </summary>
        internal static readonly Dictionary<PropertyKey, EditingProperty> PropertyFromKey = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty"/> class.
        /// </summary>
        /// <param name="name">プロパティの名前です.</param>
        /// <param name="owner">このプロパティを持つオブジェクトの <see cref="Type"/> です.</param>
        /// <param name="value">プロパティの値の <see cref="Type"/> です.</param>
        /// <param name="builder">プロパティの値を初期化するオブジェクトです.</param>
        internal EditingProperty(string name, Type owner, Type value, IPropertyBuilder? builder = null)
        {
            Name = name;
            OwnerType = owner;
            ValueType = value;
            Builder = builder;
        }

        /// <summary>
        /// Gets the name of the property.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the owner type of the property.
        /// </summary>
        public Type OwnerType { get; }

        /// <summary>
        /// Gets the value type of the property.
        /// </summary>
        public Type ValueType { get; }

        /// <summary>
        /// Gets the <see cref="IPropertyBuilder"/> that initializes the local value of a property.
        /// </summary>
        public IPropertyBuilder? Builder { get; }

        /// <summary>
        /// Registers a editor property with the specified property name, value type, and owner type.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="builder">The <see cref="IPropertyBuilder{T}"/> that initializes the local value of a property.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> Register<TValue, TOwner>(string name, IPropertyBuilder<TValue>? builder = null)
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner));

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(ExceptionMessage.KeyHasAlreadyBeenRegisterd);
            }
            var property = new EditingProperty<TValue>(name, typeof(TOwner), builder);

            PropertyFromKey.Add(key, property);

            return property;
        }

        /// <summary>
        /// <see cref="BEditor.Data.EditingProperty.PropertyFromKey"/> のキーです.
        /// </summary>
        /// <param name="Name">The name of the property.</param>
        /// <param name="OwnerType">The owner type of the property.</param>
        internal record PropertyKey(string Name, Type OwnerType);
    }
}
