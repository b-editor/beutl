using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Media;

namespace BEditor.Data.Property.Easing
{
    /// <summary>
    /// <see cref="EaseProperty"/>, <see cref="ColorAnimationProperty"/> などで利用可能なイージング関数を表します
    /// </summary>
    [DataContract]
    public abstract class EasingFunc : EditorObject, IChild<PropertyElement>, IParent<IEasingProperty>, IElementObject
    {
        #region Fields
        private WeakReference<PropertyElement?>? _parent;
        private IEnumerable<IEasingProperty>? _cachedList;
        #endregion


        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<IEasingProperty> Properties { get; }

        /// <inheritdoc/>
        public IEnumerable<IEasingProperty> Children => _cachedList ??= Properties;

        /// <inheritdoc/>
        public PropertyElement Parent
        {
            get
            {
                _parent ??= new(null!);

                if (_parent.TryGetTarget(out var p))
                {
                    return p;
                }

                return null!;
            }
            set
            {
                (_parent ??= new(null!)).SetTarget(value);

                foreach (var prop in Children)
                {
                    prop.Parent = Parent?.Parent;
                }
            }
        }

        /// <summary>
        /// Easing the value
        /// </summary>
        /// <param name="frame">frame</param>
        /// <param name="totalframe">total frame</param>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Eased value</returns>
        public abstract float EaseFunc(Frame frame, Frame totalframe, float min, float max);
    }
}
