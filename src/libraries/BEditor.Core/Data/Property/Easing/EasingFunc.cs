// EasingFunc.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;

using BEditor.Media;

namespace BEditor.Data.Property.Easing
{
    /// <summary>
    /// Represents an easing function that can be used with <see cref="IKeyframeProperty"/>.
    /// </summary>
    public abstract class EasingFunc : EditingObject, IChild<PropertyElement>, IParent<IEasingProperty>
    {
        private PropertyElement? _parent;
        private IEnumerable<IEasingProperty>? _cachedList;

        /// <inheritdoc/>
        public IEnumerable<IEasingProperty> Children => _cachedList ??= GetProperties().ToArray();

        /// <inheritdoc/>
        public PropertyElement Parent
        {
            get => _parent!;
            set
            {
                _parent = value;
                foreach (var prop in Children)
                {
                    prop.Parent = Parent?.Parent!;
                }
            }
        }

        /// <summary>
        /// Gets the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        /// <returns>Returns the <see cref="PropertyElement"/> to display on the GUI.</returns>
        public abstract IEnumerable<IEasingProperty> GetProperties();

        /// <summary>
        /// Easing the value.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="totalframe">The total frame.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>Eased value.</returns>
        public abstract float EaseFunc(Frame frame, Frame totalframe, float min, float max);

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            foreach (var item in GetType().GetProperties()
                .Where(i => Attribute.IsDefined(i, typeof(DataMemberAttribute)))
                .Select(i => (Info: i, Attribute: (DataMemberAttribute)Attribute.GetCustomAttribute(i, typeof(DataMemberAttribute))!))
                .Select(i => (Object: i.Info.GetValue(this), i)))
            {
                if (item.Object is IJsonObject json)
                {
                    writer.WriteStartObject(item.i.Attribute.Name ?? item.i.Info.Name);
                    json.GetObjectData(writer);
                    writer.WriteEndObject();
                }
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            foreach (var item in GetType().GetProperties()
                .Where(i => Attribute.IsDefined(i, typeof(DataMemberAttribute)))
                .Select(i => (Info: i, Attribute: (DataMemberAttribute)Attribute.GetCustomAttribute(i, typeof(DataMemberAttribute))!))
                .Where(i => i.Info.PropertyType.IsAssignableTo(typeof(IJsonObject))))
            {
                var property = element.GetProperty(item.Attribute.Name ?? item.Info.Name);
                var obj = (IJsonObject)FormatterServices.GetUninitializedObject(item.Info.PropertyType);
                obj.SetObjectData(property);

                if (item.Info.ReflectedType != item.Info.DeclaringType)
                {
                    item.Info.DeclaringType?.GetProperty(item.Info.Name)?.SetValue(this, obj);
                }
                else
                {
                    item.Info.SetValue(this, obj);
                }
            }
        }
    }
}