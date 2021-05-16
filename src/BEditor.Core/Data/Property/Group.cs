using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => _cachedList ??= GetProperties().ToArray();

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

        /// <summary>
        /// Gets the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> GetProperties();
    }
}