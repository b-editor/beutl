using System;
using System.Collections.Generic;
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

                for (int i = 0; i < GroupItems.Count; i++) {
                    GroupItems[i].Parent = value;
                }
            }
        }

        /// <inheritdoc/>
        public override void PropertyLoaded() {
            base.PropertyLoaded();

            var g = GroupItems;

            Parallel.For(0, GroupItems.Count, index => g[index].PropertyLoaded());

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            Parallel.For(0, properties.Length, index => {
                var property = properties[index];

                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
        }
    }
}
