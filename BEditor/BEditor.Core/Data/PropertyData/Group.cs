using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// <see cref="PropertyElement"/> をまとめるクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class Group : PropertyElement, IKeyFrameProperty, IEasingSetting {
        /// <summary>
        /// グループにする <see cref="PropertyElement"/> を取得します
        /// </summary>
        public abstract IList<PropertyElement> GroupItems { get; }

        /// <inheritdoc/>
        public override EffectElement Parent {
            get => base.Parent;
            set {
                base.Parent = value;
                var items = GroupItems;

                Parallel.ForEach(items, item => item.Parent = value);
            }
        }

        /// <inheritdoc/>
        public override void PropertyLoaded() {
            base.PropertyLoaded();
            var g = GroupItems;

            Parallel.ForEach(g, item => item.PropertyLoaded());

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            Parallel.ForEach(properties, property => {
                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
        }
    }
}
