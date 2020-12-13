using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.Control;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// <see cref="PropertyElement"/> をまとめるクラス
    /// </summary>
    [DataContract]
    public abstract class Group : PropertyElement, IKeyFrameProperty, IEasingProperty, IParent<PropertyElement>
    {
        private IEnumerable<PropertyElement> cachedlist;
        
        /// <summary>
        /// グループにする <see cref="PropertyElement"/> を取得します
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <summary>
        /// キャッシュされた <see cref="Properties"/> を取得します
        /// </summary>
        public IEnumerable<PropertyElement> Children => cachedlist ??= Properties;

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
    }
}
