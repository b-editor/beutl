using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Media;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a base class for grouping <see cref="PropertyElement"/>.
    /// </summary>
    [DataContract]
    public abstract class Group : PropertyElement, IKeyFrameProperty, IEasingProperty, IParent<PropertyElement>
    {
        private IEnumerable<PropertyElement>? _CachedList;

        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => _CachedList ??= Properties;

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
        EasingFunc? IKeyFrameProperty.EasingType => null;
        List<Frame> IKeyFrameProperty.Frames => new(0);
        event Action<Frame, int>? IKeyFrameProperty.Added
        {
            add
            {
            }
            remove
            {
            }
        }
        event Action<int>? IKeyFrameProperty.Removed
        {
            add
            {
            }
            remove
            {
            }
        }
        event Action<int, int>? IKeyFrameProperty.Moved
        {
            add
            {

            }
            remove
            {

            }
        }
        IRecordCommand IKeyFrameProperty.AddFrame(Frame frame)
        {
            return RecordCommand.Empty;
        }
        IRecordCommand IKeyFrameProperty.MoveFrame(int fromIndex, Frame toFrame)
        {
            return RecordCommand.Empty;
        }
        IRecordCommand IKeyFrameProperty.RemoveFrame(Frame frame)
        {
            return RecordCommand.Empty;
        }
        #endregion

        // Todo: 
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
    }
}
