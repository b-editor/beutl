// EditingObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

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
        public static readonly DirectProperty<EditingObject, Guid> IdProperty = EditingProperty.RegisterDirect<Guid, EditingObject>(
            "Id,ID",
            owner => owner.Id,
            (owner, obj) => owner.Id = obj,
            EditingPropertyOptions<Guid>.Create()
                .Serialize()
                .Initialize(() => Guid.NewGuid()));

        private Dictionary<int, object?>? _values = new();

        private Type? _ownerType;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingObject"/> class.
        /// </summary>
        protected EditingObject()
        {
            // static コンストラクターを呼び出す
            InvokeStaticInititlizer();

            // DirectPropertyかつInitializerがnullじゃない
            foreach (var prop in GetInitializable())
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

        private Dictionary<int, object?> Values => _values ??= new();

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

            if (!Values.ContainsKey(property.Id))
            {
                var value = property.Initializer is null ? default! : property.Initializer.Create();

                Values.Add(property.Id, value);

                return value;
            }

            return (TValue)Values[property.Id]!;
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

            if (!Values.ContainsKey(property.Id))
            {
                var value = property.Initializer?.Create();
                Values.Add(property.Id, value);

                return value;
            }

            return Values[property.Id];
        }

        /// <inheritdoc/>
        public void SetValue<TValue>(EditingProperty<TValue> property, TValue value)
        {
            if (CheckOwnerType(this, property))
            {
                throw new DataException(Strings.TheOwnerTypeDoesNotMatch);
            }

            var old = (TValue?)default;
            if (property is IDirectProperty<TValue> directProp)
            {
                old = directProp.Get(this);
                directProp.Set(this, value);

                goto RaiseEvent;
            }

            if (AddIfNotExist(property, value))
            {
                goto RaiseEvent;
            }

            old = (TValue?)Values[property.Id];
            Values[property.Id] = value;

        RaiseEvent:
            RaisePropertyChanged(property, old, value);
        }

        /// <inheritdoc/>
        public void SetValue(EditingProperty property, object? value)
        {
            if (value is not null && CheckValueType(property, value))
                throw new DataException(string.Format(Strings.TheValueWasNotTypeButType, property.ValueType, value.GetType()));

            if (CheckOwnerType(this, property)) throw new DataException(Strings.TheOwnerTypeDoesNotMatch);

            object? old = null;
            if (property is IDirectProperty directProp)
            {
                old = directProp.Get(this);
                directProp.Set(this, value!);

                goto RaiseEvent;
            }

            if (AddIfNotExist(property, value))
            {
                goto RaiseEvent;
            }

            old = Values[property.Id];
            Values[property.Id] = value;

        RaiseEvent:
            RaisePropertyChanged(property, old, value);
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

            foreach (var value in Values
                .Select(i => EditingPropertyRegistry.FindRegistered(i.Key))
                .Where(i => i?.IsDisposable == true)
                .ToArray())
            {
                if (Values.ContainsKey(value!.Id))
                {
                    (Values[value.Id] as IDisposable)?.Dispose();
                    Values.Remove(value.Id);
                }
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
                foreach (var prop in EditingPropertyRegistry.GetRegistered(OwnerType))
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
            foreach (var prop in GetSerializable())
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

            foreach (var prop in GetSerializable())
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

        /// <summary>
        /// Sets the backing field for a direct avalonia property, raising the <see cref="BasePropertyChanged.PropertyChanged"/> event if the value has changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="property">The property.</param>
        /// <param name="field">The backing field.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// True if the value changed, otherwise false.
        /// </returns>
        protected bool SetAndRaise<T>(EditingProperty<T> property, ref T field, T value)
        {
            if (value == null || !value.Equals(field))
            {
                field = value;

                if (property.NotifyPropertyChanged)
                {
                    RaisePropertyChanged(new PropertyChangedEventArgs(property.Name));
                }

                return true;
            }
            else
            {
                return false;
            }
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
            if (!Values.ContainsKey(property.Id))
            {
                Values.Add(property.Id, value);

                return true;
            }

            return false;
        }

        private bool AddIfNotExist<TValue>(EditingProperty property, TValue value)
        {
            if (!Values.ContainsKey(property.Id))
            {
                Values.Add(property.Id, value);

                return true;
            }

            return false;
        }

        private IEnumerable<EditingProperty> GetInitializable()
        {
            return EditingPropertyRegistry.GetRegistered(OwnerType)
                .Where(i => i is IDirectProperty && i.Initializer is not null);
        }

        private IEnumerable<EditingProperty> GetSerializable()
        {
            return EditingPropertyRegistry.GetRegistered(OwnerType)
                .Where(i => i.Serializer is not null);
        }

        private void RaisePropertyChanged(EditingProperty property, object? old, object? @new)
        {
            if (property.NotifyPropertyChanged && @new?.Equals(old) != true)
            {
                RaisePropertyChanged(new(property.Name));
            }
        }

        private void RaisePropertyChanged<TValue>(EditingProperty property, TValue? old, TValue? @new)
        {
            if (property.NotifyPropertyChanged)
            {
                if (@new == null || !@new.Equals(old))
                {
                    RaisePropertyChanged(new(property.Name));
                }
            }
        }

        private void InvokeStaticInititlizer()
        {
            var t = OwnerType;

            while (t != null)
            {
                RuntimeHelpers.RunClassConstructor(t.TypeHandle);

                t = t.BaseType;
            }
        }
    }
}