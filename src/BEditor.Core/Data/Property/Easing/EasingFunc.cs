using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Media;

namespace BEditor.Core.Data.Property.Easing
{
    /// <summary>
    /// <see cref="EaseProperty"/>, <see cref="ColorAnimationProperty"/> などで利用可能なイージング関数を表します
    /// </summary>
    [DataContract]
    public abstract class EasingFunc : EditorObject, IChild<PropertyElement>, IParent<IEasingProperty>, IElementObject
    {
        #region Fields
        private PropertyElement? _Parent;
        private IEnumerable<IEasingProperty>? _CachedList;
        #endregion


        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<IEasingProperty> Properties { get; }

        /// <inheritdoc/>
        public IEnumerable<IEasingProperty> Children => _CachedList ??= Properties;

        /// <inheritdoc/>
        public PropertyElement? Parent
        {
            get => _Parent;
            set
            {
                if (value is null) throw new ArgumentNullException(nameof(value));

                _Parent = value;
                var parent_ = value.Parent;

                Parallel.ForEach(Children, item => item.Parent = parent_);
            }
        }
        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// Easing the value
        /// </summary>
        /// <param name="frame">frame</param>
        /// <param name="totalframe">total frame</param>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Eased value</returns>
        public abstract float EaseFunc(Frame frame, Frame totalframe, float min, float max);

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            OnLoad();

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            OnUnload();

            IsLoaded = false;
        }

        /// <inheritdoc cref="Load"/>
        protected virtual void OnLoad() { }
        /// <inheritdoc cref="Unload"/>
        protected virtual void OnUnload() { }
    }
}
