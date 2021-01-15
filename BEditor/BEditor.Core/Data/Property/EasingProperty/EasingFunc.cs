using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Primitive.Properties.PrimitiveEasing;
using BEditor.Media;

namespace BEditor.Core.Data.Property.EasingProperty
{
    /// <summary>
    /// <see cref="EaseProperty"/>, <see cref="ColorAnimationProperty"/> などで利用可能なイージング関数を表します
    /// </summary>
    [DataContract]
    public abstract class EasingFunc : ComponentObject, IChild<PropertyElement>, IParent<IEasingProperty>, IElementObject
    {
        #region Fields

        private PropertyElement parent;
        private IEnumerable<IEasingProperty> cachedlist;

        #endregion
        

        /// <summary>
        /// UIに表示するプロパティを取得します
        /// </summary>
        public abstract IEnumerable<IEasingProperty> Properties { get; }

        /// <summary>
        /// キャッシュされた <see cref="Properties"/> を取得します
        /// </summary>
        public IEnumerable<IEasingProperty> Children => cachedlist ??= Properties;

        /// <summary>
        /// 親要素を取得します
        /// </summary>
        public PropertyElement Parent
        {
            get => parent;
            set
            {
                parent = value;
                var parent_ = parent.Parent;

                Parallel.ForEach(Children, item => item.Parent = parent_);
            }
        }

        /// <summary>
        /// 読み込まれているイージング関数のType
        /// </summary>
        public static List<EasingData> LoadedEasingFunc { get; } = new List<EasingData>() {
            new EasingData() { Name = "デフォルト", Type = typeof(DefaultEasing) }
        };
        public bool IsLoaded { get; protected set; }


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
        public virtual void Loaded() { }
        /// <inheritdoc/>
        public virtual void Unloaded() { }
    }

    public class EasingData
    {
        public string Name { get; set; }
        public Func<EasingFunc> CreateFunc { get; set; }
        public Type Type { get; set; }
    }
}
