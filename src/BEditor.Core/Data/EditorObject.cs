using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Threading;

using BEditor.Data.Property;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the edited data.
    /// </summary>
    public interface IEditorObject : INotifyPropertyChanged, IExtensibleDataObject, IElementObject
    {
        /// <summary>
        /// Gets the synchronization context for this object.
        /// </summary>
        public SynchronizationContext? Synchronize { get; }
        /// <summary>
        /// Gets the ServiceProvider.
        /// </summary>
        public ServiceProvider? ServiceProvider { get; }

        /// <summary>
        /// Gets or sets the local value of <see cref="EditorProperty"/>.
        /// </summary>
        /// <param name="property">The <see cref="EditorProperty"/> identifier of the property whose value is to be set or retrieved.</param>
        /// <returns>Returns the current effective value.</returns>
        public object? this[EditorProperty property] { get; set; }

        /// <summary>
        /// Gets the local value of <see cref="EditorProperty{TValue}"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <param name="property">The <see cref="EditorProperty{TValue}"/> identifier of the property to retrieve the value for.</param>
        /// <returns>Returns the current effective value.</returns>
        public TValue GetValue<TValue>(EditorProperty<TValue> property);
        /// <summary>
        /// Gets the local value of <see cref="EditorProperty"/>.
        /// </summary>
        /// <param name="property">The <see cref="EditorProperty"/> identifier of the property to retrieve the value for.</param>
        /// <returns>Returns the current effective value.</returns>
        public object? GetValue(EditorProperty property);
        /// <summary>
        /// Sets the local value of <see cref="EditorProperty{TValue}"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <param name="property">The identifier of the <see cref="EditorProperty{TValue}"/> to set.</param>
        /// <param name="value">The new local value.</param>
        public void SetValue<TValue>(EditorProperty<TValue> property, TValue value);
        /// <summary>
        /// Sets the local value of <see cref="EditorProperty"/>.
        /// </summary>
        /// <param name="property">The identifier of the <see cref="EditorProperty"/> to set.</param>
        /// <param name="value">The new local value.</param>
        public void SetValue(EditorProperty property, object? value);
    }
    /// <summary>
    /// Represents the base class of the edit data.
    /// </summary>
    [DataContract]
    public class EditorObject : BasePropertyChanged, IEditorObject
    {
        private Dictionary<string, object?>? _values = new();


        /// <inheritdoc/>
        public SynchronizationContext? Synchronize { get; private set; } = SynchronizationContext.Current;

        /// <inheritdoc/>
        public virtual ExtensionDataObject? ExtensionData
        {
            get => null;
            set => Synchronize = SynchronizationContext.Current;
        }

        /// <inheritdoc/>
        public ServiceProvider? ServiceProvider { get; internal set; }

        private Dictionary<string, object?> Values => _values ??= new();

        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        /// <inheritdoc/>
        public object? this[EditorProperty property]
        {
            get => GetValue(property);
            set => SetValue(property, value);
        }


        /// <inheritdoc/>
        public TValue GetValue<TValue>(EditorProperty<TValue> property)
        {
            return (TValue)GetValue((EditorProperty)property)!;
        }

        /// <inheritdoc/>
        public object? GetValue(EditorProperty property)
        {
            if (!Values.ContainsKey(property.Name))
            {
                var value = property.Builder?.Build();
                Values.Add(property.Name, value);

                return value;
            }

            return Values[property.Name];
        }

        /// <inheritdoc/>
        public void SetValue<TValue>(EditorProperty<TValue> property, TValue value)
        {
            SetValue((EditorProperty)property, value);
        }

        /// <inheritdoc/>
        public void SetValue(EditorProperty property, object? value)
        {
            var valueType = value?.GetType();
            if (!(property.ValueType == valueType || (valueType?.IsSubclassOf(property.ValueType) ?? false)))
            {
                throw new DataException($"The value was not {property.ValueType} type, but {valueType} type.");
            }

            if (!Values.ContainsKey(property.Name))
            {
                Values.Add(property.Name, value);

                return;
            }

            Values[property.Name] = value;
        }

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            if (this is IChild<EditorObject> obj1)
            {
                ServiceProvider = obj1.Parent?.ServiceProvider;
            }

            if (this is IChild<IApplication> child_app)
            {
                ServiceProvider = child_app.Parent?.Services.BuildServiceProvider();
            }

            if (this is IParent<EditorObject> obj2)
            {
                var owner = GetType();
                foreach (var prop in EditorProperty.PropertyFromKey
                    .Where(i => owner.IsSubclassOf(i.Value.OwnerType) || owner == i.Value.OwnerType)
                    .Select(i => i.Value))
                {
                    var value = this[prop];
                    if (value is PropertyElement p && prop.Builder is PropertyElementMetadata pmeta)
                    {
                        p.PropertyMetadata = pmeta;
                    }
                }

                foreach (var item in obj2.Children)
                {
                    item.Load();
                }
            }

            OnLoad();

            IsLoaded = true;
        }

        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            if (this is IParent<EditorObject> obj)
            {
                foreach (var item in obj.Children)
                {
                    item.Unload();
                }
            }

            OnUnload();

            IsLoaded = false;
        }

        /// <inheritdoc cref="IElementObject.Load"/>
        protected virtual void OnLoad()
        {

        }

        /// <inheritdoc cref="IElementObject.Unload"/>
        protected virtual void OnUnload()
        {

        }
    }

    /// <summary>
    /// Represents the ability to create an instance of a local value of <see cref="EditorProperty"/>.
    /// </summary>
    public interface IPropertyBuilder
    {
        /// <summary>
        /// Create a local value instance of <see cref="EditorProperty"/>.
        /// </summary>
        /// <returns>Returns the created local value.</returns>
        public object Build();
    }
    /// <summary>
    /// Represents the ability to create an instance of a local value of <see cref="EditorProperty{TValue}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the local value.</typeparam>
    public interface IPropertyBuilder<T> : IPropertyBuilder
    {
        object IPropertyBuilder.Build()
        {
            return Build()!;
        }
        /// <summary>
        /// Create a local value instance of <see cref="EditorProperty{TValue}"/>.
        /// </summary>
        /// <returns>Returns the created local value.</returns>
        public new T Build();
    }

    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public class EditorProperty
    {
        internal static readonly Dictionary<PropertyKey, EditorProperty> PropertyFromKey = new();


        internal EditorProperty(string name, Type owner, Type value, IPropertyBuilder? builder = null)
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
        /// <returns>Returns the registered <see cref="EditorProperty{TValue}"/>.</returns>
        public static EditorProperty<TValue> Register<TValue, TOwner>(string name, IPropertyBuilder<TValue>? builder = null) where TOwner : IEditorObject
        {
            var key = new PropertyKey(name, typeof(TOwner));

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException("This key has already been registered.");
            }
            var property = new EditorProperty<TValue>(name, typeof(TOwner), builder);

            PropertyFromKey.Add(key, property);

            return property;
        }


        internal record PropertyKey(string Name, Type OwnerType);
    }

    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the local value.</typeparam>
    public class EditorProperty<TValue> : EditorProperty
    {
        internal EditorProperty(string name, Type owner, IPropertyBuilder<TValue>? builder = null) : base(name, owner, typeof(TValue), builder)
        {

        }

        /// <summary>
        /// Gets the <see cref="IPropertyBuilder{T}"/> that initializes the local value of a property.
        /// </summary>
        public new IPropertyBuilder<TValue>? Builder => base.Builder as IPropertyBuilder<TValue>;
    }
}
