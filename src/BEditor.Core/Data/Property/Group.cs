using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Property.Easing;
using BEditor.Media;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a base class for grouping <see cref="PropertyElement"/>.
    /// </summary>
    public abstract class Group : PropertyElement, IKeyframeProperty, IEasingProperty, IParent<PropertyElement>
    {
        private IEnumerable<PropertyElement>? _cachedList;

        /// <inheritdoc/>
        event Action<Frame, int>? IKeyframeProperty.Added
        {
            add
            {
            }
            remove
            {
            }
        }

        /// <inheritdoc/>
        event Action<int>? IKeyframeProperty.Removed
        {
            add
            {
            }
            remove
            {
            }
        }

        /// <inheritdoc/>
        event Action<int, int>? IKeyframeProperty.Moved
        {
            add
            {
            }
            remove
            {
            }
        }

        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }

        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => _cachedList ??= Properties;

        /// <inheritdoc/>
        public override EffectElement Parent
        {
            get => base.Parent;
            set
            {
                base.Parent = value;

                Parallel.ForEach(Children, item => item.Parent = value);
            }
        }

        #region IkeyframeProperty

        /// <inheritdoc/>
        EasingFunc? IKeyframeProperty.EasingType => null;

        /// <inheritdoc/>
        List<Frame> IKeyframeProperty.Frames => new(0);

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.AddFrame(Frame frame)
        {
            return RecordCommand.Empty;
        }

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.MoveFrame(int fromIndex, Frame toFrame)
        {
            return RecordCommand.Empty;
        }

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.RemoveFrame(Frame frame)
        {
            return RecordCommand.Empty;
        }
        #endregion

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