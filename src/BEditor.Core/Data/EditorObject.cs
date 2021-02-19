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
#pragma warning disable CS1591
    /// <summary>
    /// Represents the base class of the edit data.
    /// </summary>
    [DataContract]
    public class EditorObject : BasePropertyChanged, IExtensibleDataObject, IElementObject
    {
        private Dictionary<string, dynamic>? _ComponentData;
        private Dictionary<string, object?>? _values = new();


        /// <summary>
        /// Gets the synchronization context for this object.
        /// </summary>
        public SynchronizationContext? Synchronize { get; private set; } = SynchronizationContext.Current;

        /// <summary>
        /// Get a Dictionary to put the cache in.
        /// </summary>
        public Dictionary<string, dynamic> ComponentData => _ComponentData ??= new Dictionary<string, dynamic>();

        /// <inheritdoc/>
        public virtual ExtensionDataObject? ExtensionData
        {
            get => null;
            set => Synchronize = SynchronizationContext.Current;
        }

        /// <summary>
        /// Gets the ServiceProvider.
        /// </summary>
        public ServiceProvider? ServiceProvider { get; internal set; }

        private Dictionary<string, object?> Values => _values ??= new();

        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        public object? this[EditorProperty property]
        {
            get => GetValue(property);
            set => SetValue(property, value);
        }


        public TValue GetValue<TValue>(EditorProperty<TValue> property)
        {
            return (TValue)GetValue((EditorProperty)property)!;
        }

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

        public void SetValue<TValue>(EditorProperty<TValue> property, TValue value)
        {
            SetValue((EditorProperty)property, value);
        }

        public void SetValue(EditorProperty property, object? value)
        {
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

            if(this is IChild<IApplication> child_app)
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

    public interface IPropertyBuilder
    {
        public object Build();
    }
    public interface IPropertyBuilder<T> : IPropertyBuilder
    {
        object IPropertyBuilder.Build()
        {
            return Build()!;
        }
        public new T Build();
    }

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

        public string Name { get; }
        public Type OwnerType { get; }
        public Type ValueType { get; }
        public IPropertyBuilder? Builder { get; }

        public static EditorProperty<TValue> Register<TValue, TOwner>(string name, IPropertyBuilder<TValue>? builder = null)
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

    public class EditorProperty<TValue> : EditorProperty
    {
        internal EditorProperty(string name, Type owner, IPropertyBuilder<TValue>? builder = null) : base(name, owner, typeof(TValue), builder)
        {

        }

        public new IPropertyBuilder<TValue>? Builder => base.Builder as IPropertyBuilder<TValue>;
    }
#pragma warning restore CS1591
}
