// EditingObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using BEditor.Data.Internals;
using BEditor.Resources;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the base class of the edit data.
    /// </summary>
    public class EditingObject : BasePropertyChanged, IEditingObject, IJsonObject
    {
        /// <summary>
        /// Defines the <see cref="Id"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<EditingObject, Guid> IdProperty = EditingProperty.RegisterDirect<Guid, EditingObject>(
            "Id,ID",
            owner => owner.Id,
            (owner, obj) => owner.Id = obj,
            EditingPropertyOptions<Guid>.Create()
                .Serialize()
                .Initialize(() => Guid.NewGuid()));

        private Dictionary<EditingPropertyRegistryKey, object?>? _values = new();

        private Type? _ownerType;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingObject"/> class.
        /// </summary>
        protected EditingObject()
        {
            // static コンストラクターを呼び出す
            InvokeStaticInititlizer();

            // DirectEditingPropertyかつInitializerがnullじゃない
            foreach (var prop in EditingPropertyRegistry.GetInitializableProperties(OwnerType))
            {
                if (prop is IDirectProperty direct && direct.Get(this) is null)
                {
                    direct.Set(this, direct.Initializer!.Create());
                }
            }
        }

        /// <inheritdoc/>
        public IServiceProvider? ServiceProvider { get; internal set; }

        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        /// <inheritdoc/>
        public Guid Id { get; protected set; } = Guid.NewGuid();

        private Dictionary<EditingPropertyRegistryKey, object?> Values => _values ??= new();

        private Type OwnerType => _ownerType ??= GetType();

        /// <inheritdoc/>
        public object? this[EditingProperty property]
        {
            get => GetValue(property);
            set => SetValue(property, value);
        }

        /// <inheritdoc/>
        public TValue GetValue<TValue>(EditingProperty<TValue> property)
        {
            if (CheckOwnerType(this, property))
            {
                throw new DataException(Strings.TheOwnerTypeDoesNotMatch);
            }

            if (property is IDirectProperty<TValue> directProp)
            {
                var value = directProp.Get(this);
                if (value is null)
                {
                    value = directProp.Initializer is null ? default! : directProp.Initializer.Create();

                    if (value is not null)
                    {
                        directProp.Set(this, value);
                    }
                }

                return value;
            }

            if (!Values.ContainsKey(property.Key))
            {
                var value = property.Initializer is null ? default! : property.Initializer.Create();

                Values.Add(property.Key, value);

                return value;
            }

            return (TValue)Values[property.Key]!;
        }

        /// <inheritdoc/>
        public object? GetValue(EditingProperty property)
        {
            if (CheckOwnerType(this, property))
            {
                throw new DataException(Strings.TheOwnerTypeDoesNotMatch);
            }

            if (property is IDirectProperty directProp)
            {
                var value = directProp.Get(this);
                if (value is null)
                {
                    value = directProp.Initializer?.Create();

                    if (value is not null)
                    {
                        directProp.Set(this, value);
                    }
                }

                return value;
            }

            if (!Values.ContainsKey(property.Key))
            {
                var value = property.Initializer?.Create();
                Values.Add(property.Key, value);

                return value;
            }

            return Values[property.Key];
        }

        /// <inheritdoc/>
        public void SetValue<TValue>(EditingProperty<TValue> property, TValue value)
        {
            if (CheckOwnerType(this, property))
            {
                throw new DataException(Strings.TheOwnerTypeDoesNotMatch);
            }

            if (property is IDirectProperty<TValue> directProp)
            {
                directProp.Set(this, value);

                return;
            }

            if (AddIfNotExist(property, value))
            {
                return;
            }

            Values[property.Key] = value;
        }

        /// <inheritdoc/>
        public void SetValue(EditingProperty property, object? value)
        {
            if (value is not null && CheckValueType(property, value))
                throw new DataException(string.Format(Strings.TheValueWasNotTypeButType, property.ValueType, value.GetType()));

            if (CheckOwnerType(this, property)) throw new DataException(Strings.TheOwnerTypeDoesNotMatch);

            if (property is IDirectProperty directProp)
            {
                directProp.Set(this, value!);

                return;
            }

            if (AddIfNotExist(property, value))
            {
                return;
            }

            Values[property.Key] = value;
        }

        /// <inheritdoc/>
        public void ClearDisposable()
        {
            static void ClearChildren(EditingObject @object)
            {
                if (@object is IParent<IEditingObject> parent)
                {
                    foreach (var child in parent.Children)
                    {
                        child.ClearDisposable();
                    }
                }
            }

            foreach (var value in Values.Where(i => i.Key.IsDisposable).ToArray())
            {
                (value.Value as IDisposable)?.Dispose();
                Values.Remove(value.Key);
            }

            ClearChildren(this);
        }

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            if (this is IChild<IEditingObject> obj1)
            {
                ServiceProvider = obj1.Parent?.ServiceProvider;
            }

            if (this is IChild<ITopLevel> child_app)
            {
                ServiceProvider = child_app.Parent?.Services.BuildServiceProvider();
            }

            OnLoad();

            if (this is IParent<IEditingObject> obj2)
            {
                foreach (var prop in EditingPropertyRegistry.GetProperties(OwnerType))
                {
                    if (prop.Initializer is not null && this[prop] is IHasMetadata value)
                    {
                        value.Metadata ??= prop.Initializer;
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

            if (this is IParent<IEditingObject> obj)
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
            foreach (var prop in EditingPropertyRegistry.GetSerializableProperties(OwnerType))
            {
                var value = GetValue(prop);

                if (value is not null)
                {
                    writer.Write(prop, value);
                }
            }
        }

        /// <inheritdoc/>
        public virtual void SetObjectData(JsonElement element)
        {
            // static コンストラクターを呼び出す
            InvokeStaticInititlizer();

            foreach (var prop in EditingPropertyRegistry.GetSerializableProperties(OwnerType))
            {
                SetValue(prop, element.Read(prop));
            }
        }

        /// <inheritdoc/>
        public void UpdateId()
        {
            Id = Guid.NewGuid();

            if (this is IParent<IEditingObject> obj)
            {
                foreach (var item in obj.Children)
                {
                    item.UpdateId();
                }
            }
        }

        /// <inheritdoc cref="IElementObject.Load"/>
        protected virtual void OnLoad()
        {
        }

        /// <inheritdoc cref="IElementObject.Unload"/>
        protected virtual void OnUnload()
        {
        }

        // 値の型が一致しない場合はtrue
        private static bool CheckValueType(EditingProperty property, object value)
        {
            var valueType = value.GetType();

            return !valueType.IsAssignableTo(property.ValueType);
        }

        // オーナーの型が一致しない場合はtrue
        private static bool CheckOwnerType(EditingObject obj, EditingProperty property)
        {
            var ownerType = obj.OwnerType;

            return !ownerType.IsAssignableTo(property.OwnerType);
        }

        // 追加した場合はtrue
        private bool AddIfNotExist(EditingProperty property, object? value)
        {
            if (!Values.ContainsKey(property.Key))
            {
                Values.Add(property.Key, value);

                return true;
            }

            return false;
        }

        private bool AddIfNotExist<TValue>(EditingProperty property, TValue value)
        {
            if (!Values.ContainsKey(property.Key))
            {
                Values.Add(property.Key, value);

                return true;
            }

            return false;
        }

        private void InvokeStaticInititlizer()
        {
            OwnerType.TypeInitializer?.Invoke(null, null);
            OwnerType.BaseType?.TypeInitializer?.Invoke(null, null);
        }
    }
}