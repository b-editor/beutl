using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;

using BEditor.Data.Property;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the base class of the edit data.
    /// </summary>
    public class EditingObject : BasePropertyChanged, IEditingObject, IJsonObject
    {
        private Dictionary<string, object?>? _values = new();

        /// <inheritdoc/>
        public SynchronizationContext Synchronize { get; private set; } = AsyncOperationManager.SynchronizationContext;

        /// <inheritdoc/>
        public IServiceProvider? ServiceProvider { get; internal set; }

        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        private Dictionary<string, object?> Values => _values ??= new();

        /// <inheritdoc/>
        public object? this[EditingProperty property]
        {
            get => GetValue(property);
            set => SetValue(property, value);
        }

        /// <inheritdoc/>
        public TValue GetValue<TValue>(EditingProperty<TValue> property)
        {
            return (TValue)GetValue((EditingProperty)property)!;
        }

        /// <inheritdoc/>
        public object? GetValue(EditingProperty property)
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
        public void SetValue<TValue>(EditingProperty<TValue> property, TValue value)
        {
            SetValue((EditingProperty)property, value);
        }

        /// <inheritdoc/>
        public void SetValue(EditingProperty property, object? value)
        {
            var valueType = value?.GetType();
            if (!(property.ValueType == valueType || (valueType?.IsSubclassOf(property.ValueType) ?? false)))
            {
                throw new DataException(string.Format(ExceptionMessage.The_value_was_not_0_type_but_1_type, property.ValueType, valueType));
            }

            if (!Values.ContainsKey(property.Name))
            {
                Values.Add(property.Name, value);

                return;
            }

            Values[property.Name] = value;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            static void ClearChildren(EditingObject @object)
            {
                if (@object is IParent<IEditingObject> parent)
                {
                    foreach (var child in parent.Children)
                    {
                        child.Clear();
                    }
                }

                if (@object is IKeyFrameProperty property)
                {
                    property.EasingType?.Clear();
                }
            }

            foreach (var value in Values)
            {
                if(value.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            Values.Clear();
            ClearChildren(this);
        }

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            if (this is IChild<EditingObject> obj1)
            {
                ServiceProvider = obj1.Parent?.ServiceProvider;
            }

            if (this is IChild<IApplication> child_app)
            {
                ServiceProvider = child_app.Parent?.Services.BuildServiceProvider();
            }

            OnLoad();

            if (this is IParent<EditingObject> obj2)
            {
                var owner = GetType();
                foreach (var prop in EditingProperty.PropertyFromKey
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

            IsLoaded = true;
        }

        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            OnUnload();

            if (this is IParent<EditingObject> obj)
            {
                foreach (var item in obj.Children)
                {
                    item.Unload();
                }
            }

            IsLoaded = false;
        }

        /// <inheritdoc/>
        public virtual void GetObjectData(Utf8JsonWriter writer)
        {
        }

        /// <inheritdoc/>
        public virtual void SetObjectData(JsonElement element)
        {
            Synchronize = AsyncOperationManager.SynchronizationContext;
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
}
