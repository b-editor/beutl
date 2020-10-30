using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.NET.Data.EffectData;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.PropertyData.EasingSetting;

namespace BEditor.NET.Data.PropertyData {
    [DataContract(Namespace = "")]
    public abstract class Group : PropertyElement, IKeyFrameProperty, IEasingSetting {
        protected object CreatedKeyFrame;
        protected EffectElement parent;

        public abstract IList<PropertyElement> GroupItems { get; }

        public override EffectElement Parent {
            get => parent;
            set {
                parent = value;

                for (int i = 0; i < GroupItems.Count; i++) {
                    GroupItems[i].Parent = value;
                }
            }
        }


        public override void PropertyLoaded() {
            base.PropertyLoaded();

            var g = GroupItems;
            Parallel.For(0, GroupItems.Count, i => {
                g[i].PropertyLoaded();
            });

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            Parallel.For(0, properties.Length, i => {
                var property = properties[i];

                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
        }
    }
}
