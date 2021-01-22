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
    public abstract class EasingFunc : ComponentObject, IChild<PropertyElement>, IParent<IEasingProperty>, IElementObject
    {
        #region Fields
        private PropertyElement? _Parent;
        private IEnumerable<IEasingProperty>? _CachedList;
        #endregion


        /// <summary>
        /// UIに表示するプロパティを取得します
        /// </summary>
        public abstract IEnumerable<IEasingProperty> Properties { get; }

        /// <summary>
        /// キャッシュされた <see cref="Properties"/> を取得します
        /// </summary>
        public IEnumerable<IEasingProperty> Children => _CachedList ??= Properties;

        /// <summary>
        /// 親要素を取得します
        /// </summary>
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

        public bool IsLoaded { get; private set; }

        /// <summary>
        /// イージング関数
        /// </summary>
        /// <param name="frame">取得するフレーム</param>
        /// <param name="totalframe">全体のフレーム</param>
        /// <param name="min">最小の値</param>
        /// <param name="max">最大の値</param>
        /// <returns>イージングされた値</returns>
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

        protected virtual void OnLoad() { }
        protected virtual void OnUnload() { }
    }
}
